using Akka.Actor;
using Akka.Event;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using NEventStore;
using Scalesque;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NEventStore.Persistence;
using Failure = Akka.Actor.Failure;

namespace EventSaucing.Projectors {
    
    public abstract class Projector : ReceiveActor, IWithTimers {
        private readonly IPersistStreams _persistStreams;
        private bool _isCatchingUp;
        private OrderedEventStreamer _catchupCommitStream;

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
                public long Checkpoint { get; }

                public CurrentCheckpoint(long checkpoint) {
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
            public class AfterProjectorCheckpointStatusSet {
                public Type MyType { get; }
                public long Checkpoint { get; }

                public AfterProjectorCheckpointStatusSet(Type myType, long checkpoint) {
                    MyType = myType;
                    Checkpoint = checkpoint;
                }
            }
        }

        private const string TimerName = "persist_checkpoint";

        /// <summary>
        /// Our proceeding projectors.  Projector type -> last known checkpoint for that projector
        /// </summary>
        public Dictionary<Type,long> PreceedingProjectors { get; } = new Dictionary<Type, long>();

        public Projector(IPersistStreams persistStreams) {
            _persistStreams = persistStreams;
            ReceiveAsync<Messages.CatchUp>(ReceivedAsync);
            ReceiveAsync<OrderedCommitNotification>(ReceivedAsync);
            ReceiveAsync<Messages.PersistCheckpoint>(msg => PersistCheckpointAsync());
            Receive<Messages.SendCurrentCheckpoint>(msg => {
                try {
                    Sender.Tell(new Messages.CurrentCheckpoint(Checkpoint), Self);
                }
                catch (Exception e) {
                    Sender.Tell(new Failure { Exception = e }, Self);
                }
            });
            Receive<Messages.AfterProjectorCheckpointStatusSet> ((msg) => {
                if (PreceedingProjectors.ContainsKey(msg.MyType))
                    PreceedingProjectors[msg.MyType] = msg.Checkpoint;
            });
        }
        /// <summary>
        /// Checks if all proceeding projectors are ahead of us
        /// </summary>
        /// <returns>bool True if we have no proceeding projectors or all proceeding projectors have a higher checkpoint than us</returns>
        protected bool AllProceedingProjectorsAhead() {
            if (!PreceedingProjectors.Any()) return true;

            return PreceedingProjectors
                .Values
                .All(proceedingCheckpoint => proceedingCheckpoint > Checkpoint);
        }

        public long Checkpoint { get; private set; }

        public Option<long> InitialCheckpoint { get; protected set; }

        /// <summary>
        /// You must set InitialCheckpoint before called base.PresStart()
        /// </summary>
        protected override void PreStart() {
            SetCheckpoint(InitialCheckpoint.GetOrElse(0L));
            PersistCheckpointAsync().Wait();
            StartTimer();
        }

        /// <summary>
        /// Turns this projector into a sequenced projector. This projector's Checkpoint will never be greater than the proceeding projector.
        ///
        /// This means it's safe for this projector to access the other's readmodels
        /// </summary>
        /// <typeparam name="T"></typeparam>
        protected void PreceededBy<T>() where T : Projector {
            var type = typeof(T);
            if (!PreceedingProjectors.ContainsKey(type)) PreceedingProjectors[type] = 0L;
        }

        /// <summary>
        /// Sets the projector's checkpoint and publishes the changed event to the event stream
        /// </summary>
        /// <param name="checkpoint"></param>
        private void SetCheckpoint(long checkpoint) {
            Checkpoint = checkpoint;
            Context.System.EventStream.Publish(new Messages.AfterProjectorCheckpointStatusSet(GetType(), checkpoint));
        }

        /// <summary>
        ///     Holds the timer which periodically tells projector to persist its checkpoint
        /// </summary>
        public ITimerScheduler Timers { get; set; }

        private async Task ReceivedAsync(Messages.CatchUp arg) {
            if(!_isCatchingUp) await CatchUpAsync();
        }
        /// <summary>
        /// Starts timer to periodically persist checkpoint to db
        /// </summary>
        protected virtual void StartTimer() {
            Timers.StartPeriodicTimer(TimerName, Messages.PersistCheckpoint.Message, TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Starts the catch up process where commits are streamed from the commit store.  
        /// </summary>
        /// <returns></returns>
        protected virtual async Task CatchUpAsync() {
            _isCatchingUp = true;

            //load all commits after our current checkpoint from db
            var startingCheckpoint = Checkpoint;
            _catchupCommitStream = new OrderedEventStreamer(startingCheckpoint, _persistStreams.GetFrom(startingCheckpoint));
            await SendNextCatchUpMessageAsync();
            Context.GetLogger()
                .Info($"Catchup started from checkpoint {startingCheckpoint}");
        }
        /// <summary>
        /// Get the next commit from the commit store stream and send it to ourselves.
        /// This way we can interleave commits, and <see cref="Messages.AfterProjectorCheckpointStatusSet"/> messages from any proceeding projectors.
        /// </summary>
        /// <returns></returns>
        private async Task SendNextCatchUpMessageAsync() {
            if (_catchupCommitStream.IsFinished) {
                // we have finished catching up.  Leave catching-up state.
                _isCatchingUp = false;
                _catchupCommitStream = null;
                await PersistCheckpointAsync();

                Context.GetLogger()
                    .Info($"Catchup finished at {Checkpoint}");
            } else {
                Context.Self.Tell(_catchupCommitStream.Next());
            }
        }

        /// <summary>
        ///     Persist checkpoint to db
        /// </summary>
        /// <returns></returns>
        protected abstract Task PersistCheckpointAsync();

        /// <summary>
        ///     Projects the commit.  
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>Task</returns>
        public abstract Task ProjectAsync(ICommit commit);

        protected virtual async Task ReceivedAsync(OrderedCommitNotification msg) {
            // never go ahead of a proceeding projector
            if (!AllProceedingProjectorsAhead()) {
                if (Checkpoint <= msg.PreviousCheckpoint) {
                    // this is a commit we want but we can't project it yet as we need proceeding projector(s) to project it first
                    // schedule this commit to be resent to us in the future, hopefully in the meantime all proceeding projectors will have 
                    // projected it
                    Timers.StartSingleTimer(key: $"commitid:{msg.Commit.CommitId}", msg, TimeSpan.FromMilliseconds(500));
                }
                return;
            }

            // at this point:
            // We are behind our proceeding projectors, or we aren't a sequenced projector.
            // Therefore We are allowed to try to project this commit, if we need to

            // if commit's previous checkpoint matches our current, project
            if (Checkpoint == msg.PreviousCheckpoint) {
                //  todo projection error handling : check rules around async exceptions
                // this is the next commit for us to project
                await ProjectAsync(msg.Commit);
                SetCheckpoint(msg.Commit.CheckpointToken);
            }
            else if (Checkpoint > msg.PreviousCheckpoint) {
                // we are ahead of this commit, just drop it
                Context.GetLogger()
                    .Debug("Received a commit notification  (checkpoint {0}) behind our checkpoint ({1})",
                        msg.Commit.CheckpointToken, Checkpoint);
            } else {
                // this commit is too far ahead to project it. We have fallen behind, catch up
                if (_isCatchingUp) {
                    // we already in catch up mode and this msg was likely sent by LocalEventStreamActor
                    // we will eventually see this commit at the right time via Catchup mode, so safe to ignore this message
                } else {
                    // go into catch up mode
                    await CatchUpAsync();
                }
            }

            // If we are in catch up mode, stream the next commit to Self
            if (_isCatchingUp) {
                await SendNextCatchUpMessageAsync();
            }
        }
    }
}