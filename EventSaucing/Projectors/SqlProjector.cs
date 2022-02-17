﻿using System;
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

namespace EventSaucing.Projectors
{
    //todo need sql for createing sqlprojector persistent state, and need to alter the existing sql to deal with it
    public abstract class SqlProjector : Projector  {
        protected readonly ConventionBasedEventDispatcher _dispatcher;
        protected readonly ILogger _logger;
        protected readonly IDbService _dbService;


        /// <summary>
        ///     Should projector be set to the head checkpoint of the commit store on first ever instantiation.  If false,
        ///     projector will run through all commits in the store.  If True, projector will start at the head of the commit and
        ///     only process new commits
        /// </summary>
        private bool _initialiseAtHead;

        public SqlProjector(IPersistStreams persistStreams, ILogger logger, IConfiguration config, IDbService dbService):base(persistStreams){
            _logger = logger;
            _dbService = dbService;
            _dispatcher = new ConventionBasedEventDispatcher(this);
            Name = GetType().FullName;

            var initialiseAtHead = config.GetSection("EventSaucing:Projectors:InitialiseAtHead").Get<string[]>();
            _initialiseAtHead = initialiseAtHead.Contains(GetType().FullName);
        }

        protected override void PreStart() {
            base.PreStart();

            // restore checkpoint status from db, and initialise if no state found
            using (var conn = _dbService.GetConnection()) {
                conn.Open();

                var results =
                    conn.Query<long>(
                        "SELECT LastCheckPointToken FROM dbo.ProjectorStatus WHERE ProjectorId = @Name",
                        new { this.Name });

                //if we have a checkpoint, set it
                results.ForEach(x => InitialCheckpoint = x.ToSome());
                //todo this is bugged, use Legacy Projector impl and also the table needs s new name + create script
                // initialise at head if requested
                if (InitialCheckpoint.IsEmpty && _initialiseAtHead) {
                    InitialCheckpoint = conn.ExecuteScalar<long>("SELECT MAX(CheckpointNumber) FROM dbo.Commits").ToSome();

                }
            }
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

        protected override async Task PersistCheckpointAsync() {
            using (var con = GetProjectionDb()) {
                await con.OpenAsync();

                await con.ExecuteAsync(
                    "UPDATE [dbo].[ProjectorStatus] SET [Checkpoint] = @Checkpoint WHERE ProjectorName=@Name",
                    new { Name, Checkpoint=Checkpoint });
            }
        }

        protected Task PersistCheckpointAsync(long checkpoint, DbTransaction tx) {
            return tx.Connection.ExecuteAsync(
                "UPDATE [dbo].[ProjectorStatus] SET [Checkpoint] = @Checkpoint WHERE ProjectorName=@Name",
                new { Name, checkpoint });
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