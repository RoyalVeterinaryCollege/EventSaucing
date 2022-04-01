using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.NEventStore;
using NEventStore;
using NEventStore.Persistence;
using Serilog;

namespace EventSaucing.StreamProcessors.Projectors
{
    //todo need sql for creating sqlprojector persistent state, and need to alter the existing sql to deal with it
    public abstract class SqlProjector : StreamProcessor  {
        protected readonly ConventionBasedEventDispatcher _dispatcher;
        protected readonly ILogger _logger;


        public SqlProjector(IPersistStreams persistStreams, IStreamProcessorCheckpointPersister checkpointPersister) :base(persistStreams, checkpointPersister){
            _dispatcher = new ConventionBasedEventDispatcher(this);
        }

        public override async Task<bool> ProcessAsync(ICommit commit) {
            var projectionMethods = _dispatcher.GetProjectionMethods(commit).ToList();

            if (!projectionMethods.Any()) {
                // don't bother round tripping as we didn't do any projection for this commit
                return false;
            }
            using (var con = GetProjectionDb()) {
                await con.OpenAsync();
                // silently truncate strings larger than the destination field, otherwise we would need to LEFT every string to avoid this problem
                // https://docs.microsoft.com/en-us/sql/t-sql/statements/set-ansi-warnings-transact-sql?view=sql-server-ver15

                await con.ExecuteAsync("SET ANSI_WARNINGS OFF");
                using (var tx = con.BeginTransaction())  {
                    foreach (var (projectionMethod, @evt) in projectionMethods)  {
                        try   {
                            await projectionMethod(tx, commit, @evt);
                        }
                        catch (Exception error) {
                            _logger.Error(error.InnerException, $"{GetType().FullName} caught exception in method {projectionMethod.Method.Name} when trying to project event {@evt.GetType()} in commit {commit.CommitId}  at checkpoint {commit.CheckpointToken} for aggregate {commit.AggregateId()}");
                            throw; 
                        }
                    }
                    tx.Commit();
                    return true; // persist checkpoint
                }
            }
        }

        /// <summary>
        /// Gets the connection to where the commit will be projected and where the checkpoint will be persisted
        /// </summary>
        /// <returns></returns>
        public abstract DbConnection GetProjectionDb();
    }
}
