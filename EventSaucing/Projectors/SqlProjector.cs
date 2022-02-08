using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using Dapper;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using NEventStore;
using Scalesque;
using Serilog;

namespace EventSaucing.Projectors
{
    public abstract class SqlProjector : Projector  {
        readonly ConventionBasedEventDispatcher _dispatcher;
        protected readonly ILogger _logger;

        public SqlProjector(ILogger logger) {
            _logger = logger;
            _dispatcher = new ConventionBasedEventDispatcher(this);
            Name = GetType().FullName;
        }
        public virtual async Task Project(ICommit commit) {
            var projectionMethods = _dispatcher.GetProjectionMethods(commit).ToList();

            if (!projectionMethods.Any()) {
                LastCheckpoint = commit.CheckpointToken;
                //todo persist checkpoint state
            }

            using (var con = GetProjectionDb()) {
                await con.OpenAsync();
                // silently truncate strings larger than the destination field, otherwise we would need to LEFT every string to avoid this problem
                // https://docs.microsoft.com/en-us/sql/t-sql/statements/set-ansi-warnings-transact-sql?view=sql-server-ver15

                await con.ExecuteAsync("SET ANSI_WARNINGS OFF");  
                using (var tx = con.BeginTransaction()) {
                    foreach (var (projectionMethod,@evt) in projectionMethods)  {
                        try   {
                            await projectionMethod(tx, commit, @evt);

                            // persist new checkpoint
                            await con.ExecuteAsync(
                                "UPDATE [dbo].[ProjectorStatus] SET [Checkpoint] = @LastCheckpoint WHERE ProjectorName=@Name",
                                new { Name, LastCheckpoint });

                            LastCheckpoint = commit.CheckpointToken;
                        }
                        catch (Exception error)  {
                            _logger.Error(error.InnerException, $"{Name} caught exception when trying to project event {@evt.GetType()} in commit {commit.CommitId} at checkpoint {commit.CheckpointToken} for aggregate {commit.AggregateId()}");
                            throw;
                        }
                    }
                    tx.Commit();
                }
            }
        }

        public long LastCheckpoint { get; set; }

        /// <summary>
        /// Gets the name of the projector. Must be unique and defaults to GetType().FullName
        /// </summary>
        public virtual string Name { get; } 

        public abstract DbConnection GetProjectionDb();
        protected override Task ReceivedAsync(CatchUpMessage arg) {
            return CatchUp();
        }
        protected override async Task ReceivedAsync(OrderedCommitNotification msg) {
            //if their previous matches our current, project
            //if their previous is less than our current, ignore
            //if their previous is > our current, catchup
            var order = new CheckpointOrder();
            var comparision = order.Compare(LastCheckpoint.ToSome(), msg.PreviousCheckpoint);
            if (comparision == 0) {
                await Project(msg.Commit); //order matched, project
            }
            else if (comparision > 0) {
                //we are ahead of this commit so no-op, this is a bit odd, so log it
                Context.GetLogger()
                    .Info($"Projector {Name} received a commit notification  (checkpoint {msg.Commit.CheckpointToken} behind our checkpoint {LastCheckpoint}");
            } else {
                //we are behind the head, should catch up
                var checkpointAtStartup = LastCheckpoint;
                Context.GetLogger()
                    .Info($"Projector {Name} catchup started from checkpoint {checkpointAtStartup} after receiving out-of-sequence commit with checkpoint {msg.Commit.CheckpointToken} and previous checkpoint {msg.PreviousCheckpoint}");
                await CatchUp();
                Context.GetLogger()
                    .Info($"Projector {Name} catchup finished from {checkpointAtStartup} to checkpoint {LastCheckpoint} after receiving commit with checkpoint {msg.Commit.CheckpointToken}");
            }
        }

        private Task CatchUp() {
            throw new NotImplementedException();
        }
    }
}
