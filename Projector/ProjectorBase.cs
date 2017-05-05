using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Dapper;
using EventSaucing.Akka.Messages;
using EventSaucing.NEventStore;
using EventSaucing.Storage;
using NEventStore;
using NEventStore.Persistence;
using Scalesque;

namespace EventSaucing.Projector {
    public abstract class ProjectorBase : ReceiveActor {
        private readonly IPersistStreams _persistStreams;
        private readonly IDbService _dbService;

        public int ProjectorId { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="persistStreams">IPersistStreams Required for when the projector falls behind the head commit and needs to catchup</param>
        /// <param name="dbService"></param>
        public ProjectorBase(IPersistStreams persistStreams, IDbService dbService) {
            _persistStreams = persistStreams;
            _dbService = dbService;
            ProjectorId = this.GetProjectorId();
            Receive<CatchUpMessage>(msg => Received(msg));
            Receive<OrderedCommitNotification>(msg => Received(msg));
        }

        private void Received(CatchUpMessage msg) {
            Catchup();
        }

        protected override void PreStart() {
            base.PreStart();
            //get the head checkpoint (if there is one)
            using (var conn = _dbService.GetConnection()) {
                conn.Open();

                var results = conn.Query<long>("SELECT LastCheckPointToken FROM dbo.ProjectorStatus WHERE ProjectorId = @ProjectorId", new { this.ProjectorId });

                results.ForEach(x => {
                    Checkpoint = x.ToSome();
                });
                conn.Close();
            }
        }

        private void Received(OrderedCommitNotification msg) {
            //if their previous matches our current, project
            //if their previous is less than our current, ignore
            //if their previous is > our current, catchup
            var comparer = new CheckpointComparer();
            var comparision = comparer.Compare(Checkpoint, msg.PreviousCheckpoint);
            if (comparision == 0) {
                Project(msg.Commit); //order matched, project
            }
            else if (comparision > 0) {
                //we are ahead of this commit so no-op, this is a bit odd, so log it
                Context.GetLogger().Debug("Received a commit notification  (checkpoint {0}) behind our checkpoint ({1})", msg.Commit.CheckpointToken, Checkpoint.Get());
            }
            else {
                //we are behind the head, should catch up
                var fromPoint = Checkpoint.Map(x => x.ToString()).GetOrElse("beginning of time");
                Context.GetLogger().Info("Catchup started from checkpoint {0} after receiving out-of-sequence commit with checkpoint {1} and previous checkpoint {2}", fromPoint, msg.Commit.CheckpointTokenLong(), msg.PreviousCheckpoint);
                Catchup();
                Context.GetLogger().Info("Catchup finished from {0} to checkpoint {1} after receiving commit with checkpoint {2}", fromPoint, Checkpoint.Map(x => x.ToString()).GetOrElse("beginning of time"), msg.Commit.CheckpointTokenLong());
            }
        }

        /// <summary>
        /// Catches-up the projector if it has fallen behind the head
        /// </summary>
        protected virtual void Catchup() {
            var comparer = new CheckpointComparer();
            IEnumerable<ICommit> commits = _persistStreams.GetFrom(Checkpoint.Map(_ => _.ToString()).GetOrElse(() => null)); //load all commits after our current checkpoint from db
            foreach (var commit in commits) {
                Project(commit);
                if (comparer.Compare(Checkpoint, commit.CheckpointTokenLong().ToSome()) != 0) {
                    //something went wrong, we couldn't project
                    Context.GetLogger().Warning("Stopped catchup! was unable to project the commit at checkpoint {0}", commit.CheckpointToken);
                    break;
                }
            }
        }

        /// <summary>
        /// Derived class is responsible for updating this field after processing the new commit message
        /// </summary>
        public Option<long> Checkpoint { get; protected set; } = Option.None();

        /// <summary>
        /// Projects the commit.  The implementor MUST update the base.Checkpoint value if the commit was successful
        /// </summary>
        /// <param name="commit"></param>
        public abstract void Project(ICommit commit);
    }
}