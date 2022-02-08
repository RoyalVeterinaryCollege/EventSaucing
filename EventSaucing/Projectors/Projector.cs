using System;
using System.Threading.Tasks;
using Akka.Actor;
using EventSaucing.EventStream;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Akka.Event;
using EventSaucing.NEventStore;
using NEventStore;
using Scalesque;

namespace EventSaucing.Projectors {
    public abstract class Projector : ReceiveActor, IWithTimers {
        private const string TimerName = "persist_checkpoint";

        public static class Messages {
            /// <summary>
            /// Tell Projector to catch up by going to commit store to stream unprojected commits
            /// </summary>
            public class CatchUpMessage {
                static CatchUpMessage() {
                    Message = new CatchUpMessage();
                }
                private CatchUpMessage()  {

                }
                public static CatchUpMessage Message  {
                    get;
                }
            }
            /// <summary>
            /// Tell projector to persist its checkpoint state to db
            /// </summary>
            public class PersistCheckpoint{
                static PersistCheckpoint() {
                    Message = new PersistCheckpoint();
                }
                private PersistCheckpoint() { }
                public static PersistCheckpoint Message
                {
                    get;
                }
            }
        }
        /// <summary>
        /// Should projector be set to the head checkpoint of the commit store on first ever instantiation.  If false, projector will run through all commits in the store.  If True, projector will start at the head of the commit and only process new commits 
        /// </summary>
        protected bool _initialiseAtHead;

        /// <summary>
        /// Holds the timer which periodically tells projector to persist its checkpoint
        /// </summary>
        public ITimerScheduler Timers { get; set; }

        public Projector(IConfiguration config) {
            ReceiveAsync<Messages.CatchUpMessage>(ReceivedAsync);
            ReceiveAsync<OrderedCommitNotification>(ReceivedAsync);
            ReceiveAsync<Messages.PersistCheckpoint>(msg => PersistCheckpointAsync());

            var initialiseAtHead = config.GetSection("EventSaucing:Projectors:InitialiseAtHead").Get<string[]>();
            _initialiseAtHead = initialiseAtHead.Contains(GetType().FullName);
        }
        protected override void PreStart(){
            StartTimer();
        }

        private async Task ReceivedAsync(Messages.CatchUpMessage arg) {
            //stop timer whilst we catch up else lots of superfluous messages might pile up in mailbox
            if(Timers.IsTimerActive(TimerName)) Timers.Cancel(TimerName);
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
        /// Tells projector to project unprojected commits from the commit store
        /// </summary>
        /// <returns></returns>
        protected abstract Task CatchUpAsync();

        /// <summary>
        /// Persist checkpoint to db
        /// </summary>
        /// <returns></returns>
        protected abstract Task PersistCheckpointAsync();

        /// <summary>
        /// Projects the commit
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>Task</returns>
        public abstract Task ProjectAsync(ICommit commit);

        /// <summary>
        /// Derived class is responsible for updating this field after processing the new commit message
        /// </summary>
        public Option<long> Checkpoint { get; protected set; } = Option.None();

        protected virtual async Task ReceivedAsync(OrderedCommitNotification msg) {
            //if their previous matches our current, project
            //if their previous is less than our current, ignore
            //if their previous is > our current, catchup
            var order = new CheckpointOrder();
            var comparision = order.Compare(Checkpoint, msg.PreviousCheckpoint);
            if (comparision == 0)
            {
                await ProjectAsync(msg.Commit); //order matched, project
            }
            else if (comparision > 0)
            {
                //we are ahead of this commit so no-op, this is a bit odd, so log it
                Context.GetLogger()
                    .Debug("Received a commit notification  (checkpoint {0}) behind our checkpoint ({1})",
                        msg.Commit.CheckpointToken, Checkpoint.Get());
            }
            else
            {
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