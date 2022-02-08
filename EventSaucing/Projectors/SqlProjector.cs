﻿using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using Dapper;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using EventSaucing.Storage;
using Microsoft.Extensions.Configuration;
using NEventStore;
using Scalesque;
using Serilog;

namespace EventSaucing.Projectors
{
    public abstract class SqlProjector : Projector  {
        readonly ConventionBasedEventDispatcher _dispatcher;
        protected readonly ILogger _logger;
        private readonly IDbService _dbService;

        public SqlProjector(ILogger logger, IConfiguration config, IDbService dbService) : base(config) {
            _logger = logger;
            _dbService = dbService;
            _dispatcher = new ConventionBasedEventDispatcher(this);
            Name = GetType().FullName;
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

                results.ForEach(x => { Checkpoint = x.ToSome(); });

                // initialise at head if requested
                if (Checkpoint.IsEmpty && _initialiseAtHead) {
                    Checkpoint = conn.ExecuteScalar<long>("SELECT MAX(CheckpointNumber) FROM dbo.Commits").ToSome();
                }
            }
        }
        public override async Task ProjectAsync(ICommit commit) {
            var projectionMethods = _dispatcher.GetProjectionMethods(commit).ToList();

            if (!projectionMethods.Any()) {
                Checkpoint = commit.CheckpointToken.ToSome();
                return;
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
                            _logger.Error(error.InnerException, $"{Name} caught exception when trying to project event {@evt.GetType()} in commit {commit.CommitId} at checkpoint {commit.CheckpointToken} for aggregate {commit.AggregateId()}");
                            throw; 
                        }
                    }

                    await PersistCheckpointAsync(commit.CheckpointToken, tx);

                    tx.Commit();
                }
            }

            Checkpoint = commit.CheckpointToken.ToSome();
        }

        protected override async Task PersistCheckpointAsync() {
            using (var con = GetProjectionDb()) {
                await con.OpenAsync();

                await con.ExecuteAsync(
                    "UPDATE [dbo].[ProjectorStatus] SET [Checkpoint] = @Checkpoint WHERE ProjectorName=@Name",
                    new { Name, Checkpoint=Checkpoint.Get() });
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
