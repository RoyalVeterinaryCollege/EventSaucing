using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.NEventStore;
using EventSaucing.Storage;
using Microsoft.Extensions.Configuration;
using NEventStore;
using NEventStore.Persistence;
using Scalesque;
using Serilog;

namespace EventSaucing.StreamProcessors.Projectors
{
    //todo need sql for creating sqlprojector persistent state, and need to alter the existing sql to deal with it
    public abstract class SqlProjector : StreamProcessor  {
        protected readonly ConventionBasedEventDispatcher _dispatcher;
        protected readonly ILogger _logger;
        protected readonly IDbService _dbService;


        public SqlProjector(IPersistStreams persistStreams, IStreamProcessorCheckpointPersister checkpointPersister) :base(persistStreams, checkpointPersister){
            _dispatcher = new ConventionBasedEventDispatcher(this);
            Name = GetType().FullName;
        }

        public override async Task<bool> ProjectAsync(ICommit commit) {
            var projectionMethods = _dispatcher.GetProjectionMethods(commit).ToList();

            if (!projectionMethods.Any()) {
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
                            _logger.Error(error.InnerException, $"{Name} caught exception in method {projectionMethod.Method.Name} when trying to project event {@evt.GetType()} in commit {commit.CommitId}  at checkpoint {commit.CheckpointToken} for aggregate {commit.AggregateId()}");
                            throw; 
                        }
                    }
                    tx.Commit();
                    return true;
                }
            }
        }

   


        /// <summary>
        /// Gets the name of the projector. Must be unique and defaults to GetType().FullName
        /// </summary>
        public virtual string Name { get; } 

        /// <summary>
        /// Gets the connection to where the commit will be projected
        /// </summary>
        /// <returns></returns>
        public abstract DbConnection GetProjectionDb();
    }
}
