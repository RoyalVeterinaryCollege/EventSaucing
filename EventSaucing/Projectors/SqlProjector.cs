using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Dapper;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using NEventStore;
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
        protected override Task ReceivedAsync(OrderedCommitNotification msg) {
            throw new NotImplementedException();
        }

        protected override Task ReceivedAsync(CatchUpMessage arg) {
            throw new NotImplementedException();
        }
    }
}
