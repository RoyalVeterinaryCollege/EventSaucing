using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Event;
using Dapper;
using EventSaucing.NEventStore;
using EventSaucing.Storage;
using Microsoft.Extensions.Configuration;
using NEventStore;
using NEventStore.Persistence;
using Scalesque;
using System.Linq;

namespace EventSaucing.Projectors {

    /// <summary>
    /// Obsolete replacement for ProjectorBase.  Provided for backwards compatibility only.  Prefer <see cref="SqlProjector"/> for future usage.
    /// </summary>
    [Obsolete("Provided for backwards compatibility only.  Prefer SqlProjector for future usage")]
    public abstract class LegacyProjector : Projector {
        private readonly IPersistStreams _persistStreams;
        private protected readonly IDbService _dbService;

        public int ProjectorId { get; }

        /// <summary>
        ///     Should projector be set to the head checkpoint of the commit store on first ever instantiation.  If false,
        ///     projector will run through all commits in the store.  If True, projector will start at the head of the commit and
        ///     only process new commits
        /// </summary>
        private bool _initialiseAtHead;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="persistStreams">IPersistStreams Required for when the projector falls behind the head commit and needs to catchup</param>
        /// <param name="dbService"></param>
        /// <param name="config"></param>
        public LegacyProjector(IPersistStreams persistStreams, IDbService dbService, IConfiguration config) {
            _persistStreams = persistStreams;
            _dbService = dbService;
            ProjectorId = this.GetProjectorId();

            var initialiseAtHead = config.GetSection("EventSaucing:Projectors:InitialiseAtHead").Get<string[]>();
            _initialiseAtHead = initialiseAtHead.Contains(GetType().FullName);
        }

        protected override void PreStart() {
            base.PreStart();
            //get the head checkpoint (if there is one)
            using (var conn = _dbService.GetConnection()) {
                conn.Open();

                var results =
                    conn.Query<long>(
                        "SELECT LastCheckPointToken FROM dbo.ProjectorStatus WHERE ProjectorId = @ProjectorId",
                        new { this.ProjectorId });

                // if we have a checkpoint, set it
                results.ForEach(SetCheckpoint);

                // initialise at head if requested
                if (Checkpoint.IsEmpty && _initialiseAtHead) {
                    SetCheckpoint(conn.ExecuteScalar<long>("SELECT MAX(CheckpointNumber) FROM dbo.Commits"));
                }
            }
        }

        protected override async Task PersistCheckpointAsync()  {
            using (var conn = _dbService.GetConnection()) {
                await conn.OpenAsync();
                this.PersistProjectorCheckpoint(conn);
            }
        }

        /// <summary>
        /// Projects the commit by delegating it to the synchronous Project method
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>Task</returns>
        public override Task ProjectAsync(ICommit commit) {
            Project(commit);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Projects the commit.  Implementors are responsible for updating Checkpoint property
        /// </summary>
        /// <param name="commit"></param>
        public abstract void Project(ICommit commit);
    }
}