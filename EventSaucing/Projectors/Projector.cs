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
    public abstract class Projector : ReceiveActor {
        /// <summary>
        /// Should projector be set to the head checkpoint of the commit store on first ever instantiation.  If false, projector will run through all commits in the store.  If True, projector will start at the head of the commit and only process new commits 
        /// </summary>
        protected bool _initialiseAtHead;

        public Projector(IConfiguration config) {

            ReceiveAsync<CatchUpMessage>(ReceivedAsync);
            ReceiveAsync<OrderedCommitNotification>(ReceivedAsync);

            var initialiseAtHead = config.GetSection("EventSaucing:Projectors:InitialiseAtHead").Get<string[]>();
            _initialiseAtHead = initialiseAtHead.Contains(GetType().FullName);
        }

        /// <summary>
        /// Derived class is responsible for updating this field after processing the new commit message
        /// </summary>
        public Option<long> Checkpoint { get; protected set; } = Option.None();
        /// <summary>
        /// Projects the commit
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>Task</returns>
        public abstract Task ProjectAsync(ICommit commit);
        /// <summary>
        /// Tells projector to project unprojected commits from the commit store
        /// </summary>
        /// <returns>Task</returns>
        public abstract Task CatchUpAsync();
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

        protected virtual Task ReceivedAsync(CatchUpMessage msg) {
            return CatchUpAsync();
        }
    }
}