﻿using Akka.Actor;
using Akka.Event;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using Microsoft.Extensions.Configuration;
using NEventStore;
using Scalesque;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Failure = Akka.Actor.Failure;

namespace EventSaucing.Projectors {
    public abstract class Projector : ReceiveActor, IWithTimers {
        public static class Messages {
            /// <summary>
            ///     Tell Projector to catch up by going to commit store to stream unprojected commits
            /// </summary>
            public class CatchUp {
                static CatchUp() {
                    Message = new CatchUp();
                }

                private CatchUp() { }

                public static CatchUp Message { get; }
            }

            /// <summary>
            ///     Tell projector to persist its checkpoint state to db
            /// </summary>
            public class PersistCheckpoint {
                static PersistCheckpoint() {
                    Message = new PersistCheckpoint();
                }

                private PersistCheckpoint() { }

                public static PersistCheckpoint Message { get; }
            }

            /// <summary>
            /// Ask projector for a list of projectors it depends on
            /// </summary>
            public class SendDependUponProjectors {
                static SendDependUponProjectors() {
                    Message = new SendDependUponProjectors();
                }

                private SendDependUponProjectors() { }
                public static SendDependUponProjectors Message { get; }
            }

            /// <summary>
            /// Asks projector to send its current checkpoint. Replies with <see cref="CurrentCheckpoint"/>
            /// </summary>
            public class SendCurrentCheckpoint {
                static SendCurrentCheckpoint() {
                    Message = new SendCurrentCheckpoint();
                }

                private SendCurrentCheckpoint() { }
                public static SendCurrentCheckpoint Message { get; }
            }

            /// <summary>
            /// A reply to the <see cref="CurrentCheckpoint"/> message
            /// </summary>
            public class CurrentCheckpoint {
                public Option<long> Checkpoint { get; }

                public CurrentCheckpoint(Option<long> checkpoint) {
                    Checkpoint = checkpoint;
                }
            }

            public class DependUponProjectors {
                /// <summary>
                /// The Type of the projector that depends on the Projectors listed
                /// </summary>
                public Type MyType { get; }

                /// <summary>
                /// Reference to the projector
                /// </summary>
                public IActorRef MyRef { get; }

                /// <summary>
                /// A list of Types of projectors upon which this projector depends. If list is empty, this projector depends on no other projectors.
                /// </summary>
                public IReadOnlyList<Type> Projectors { get; }

                public DependUponProjectors(Type myType, IActorRef myRef, IReadOnlyList<Type> projectors) {
                    MyType = myType;
                    MyRef = myRef;
                    Projectors = projectors;
                }
            }

            /// <summary>
            /// Message published on EventStream after the projector's checkpoint changes
            /// </summary>
            public class AfterProjectorCheckpointStatusChanged {
                public Type MyType { get; }
                public long Checkpoint { get; }

                public AfterProjectorCheckpointStatusChanged(Type myType, long checkpoint) {
                    MyType = myType;
                    Checkpoint = checkpoint;
                }
            }
        }

        private const string TimerName = "persist_checkpoint";


        /// <summary>
        /// A  list of projectors upon which this projector is dependent
        /// </summary>
        public List<Type> DependedOnProjectors { get; } = new List<Type>();

        public Projector() {
            ReceiveAsync<Messages.CatchUp>(ReceivedAsync);
            ReceiveAsync<OrderedCommitNotification>(ReceivedAsync);
            ReceiveAsync<Messages.PersistCheckpoint>(msg => PersistCheckpointAsync());
            Receive<Messages.SendDependUponProjectors>(msg =>
                Sender.Tell(
                    new Messages.DependUponProjectors(GetType(), Context.Self, DependedOnProjectors.AsReadOnly()), Self)
            );


            Receive<Messages.SendCurrentCheckpoint>(msg => {
                try {
                    Sender.Tell(new Messages.CurrentCheckpoint(Checkpoint), Self);
                }
                catch (Exception e) {
                    Sender.Tell(new Failure { Exception = e }, Self);
                }
            });
        }

        protected override void PreStart() {
            StartTimer();
        }

        /// <summary>
        /// Turn this projector into a dependent projector.  This projector's Checkpoint will never be greater than the depended upon projector.
        ///
        /// This means it's safe for this projector to load the other's readmodels
        /// </summary>
        /// <typeparam name="T"></typeparam>
        protected void DependOn<T>() where T : Projector {
            var type = typeof(T);
            if (!DependedOnProjectors.Contains(type)) DependedOnProjectors.Add(type);
        }

        /// <summary>
        ///     Derived class is responsible for updating this field after processing the new commit message by called SetCheckpoint method
        /// </summary>
        public Option<long> Checkpoint { get; private set; } = Option.None();

        /// <summary>
        /// Sets the projector's checkpoint and publishes the changed event to the event stream
        /// </summary>
        /// <param name="checkpoint"></param>
        public void SetCheckpoint(long checkpoint) {
            Checkpoint = checkpoint.ToSome();
            Context.System.EventStream.Publish(
                new Messages.AfterProjectorCheckpointStatusChanged(GetType(), checkpoint));
        }

        /// <summary>
        ///     Holds the timer which periodically tells projector to persist its checkpoint
        /// </summary>
        public ITimerScheduler Timers { get; set; }

        private async Task ReceivedAsync(Messages.CatchUp arg) {
            //stop timer whilst we catch up else lots of superfluous messages might pile up in mailbox
            if (Timers.IsTimerActive(TimerName)) Timers.Cancel(TimerName);
            await CatchUpAsync();
            await PersistCheckpointAsync();
            StartTimer();
        }

        private void StartTimer() {
            //start timer to periodically persist checkpoint to db
            Timers.StartPeriodicTimer(TimerName, Messages.PersistCheckpoint.Message, TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));
        }

        /// <summary>
        ///     Tells projector to project unprojected commits from the commit store
        /// </summary>
        /// <returns></returns>
        protected abstract Task CatchUpAsync();

        /// <summary>
        ///     Persist checkpoint to db
        /// </summary>
        /// <returns></returns>
        protected abstract Task PersistCheckpointAsync();

        /// <summary>
        ///     Projects the commit
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>Task</returns>
        public abstract Task ProjectAsync(ICommit commit);

        protected virtual async Task ReceivedAsync(OrderedCommitNotification msg) {
            //if their previous matches our current, project
            //if their previous is less than our current, ignore
            //if their previous is > our current, catchup
            var order = new CheckpointOrder();
            var comparision = order.Compare(Checkpoint, msg.PreviousCheckpoint);
            if (comparision == 0) {
                await ProjectAsync(msg.Commit); //order matched, project
            }
            else if (comparision > 0) {
                //we are ahead of this commit so no-op, this is a bit odd, so log it
                Context.GetLogger()
                    .Debug("Received a commit notification  (checkpoint {0}) behind our checkpoint ({1})",
                        msg.Commit.CheckpointToken, Checkpoint.Get());
            }
            else {
                //we are behind the head, should catch up
                var fromPoint = Checkpoint.Map(x => x.ToString()).GetOrElse("beginning of time");
                Context.GetLogger()
                    .Info(
                        "Catchup started from checkpoint {0} after receiving out-of-sequence commit with checkpoint {1} and previous checkpoint {2}",
                        fromPoint, msg.Commit.CheckpointToken, msg.PreviousCheckpoint);
                await CatchUpAsync();
                Context.GetLogger()
                    .Info("Catchup finished from {0} to checkpoint {1} after receiving commit with checkpoint {2}",
                        fromPoint, Checkpoint.Map(x => x.ToString()).GetOrElse("beginning of time"),
                        msg.Commit.CheckpointToken);
            }
        }
    }
}