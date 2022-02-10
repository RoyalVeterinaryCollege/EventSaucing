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
        /// Instantiates
        /// </summary>
        /// <param name="persistStreams">IPersistStreams Required for when the projector falls behind the head commit and needs to catchup</param>
        /// <param name="dbService"></param>
        /// <param name="config"></param>
        public LegacyProjector(IPersistStreams persistStreams, IDbService dbService, IConfiguration config):base(config) {
            _persistStreams = persistStreams;
            _dbService = dbService;
            ProjectorId = this.GetProjectorId();
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

        /// <summary>
        /// Tells projector to project unprojected commits from the commit store
        /// </summary>
        /// <returns>Task</returns>
        protected override Task CatchUpAsync() {
            var comparer = new CheckpointOrder();
            //load all commits after our current checkpoint from db
            IEnumerable<ICommit> commits = _persistStreams.GetFrom(Checkpoint.GetOrElse(() => 0));
            foreach (var commit in commits) {
                Project(commit);
                if (comparer.Compare(Checkpoint, commit.CheckpointToken.ToSome()) != 0) {
                    //something went wrong, we couldn't project
                    Context.GetLogger().Warning("Stopped catchup! was unable to project the commit at checkpoint {0}", commit.CheckpointToken);
                    break;
                }
            }
            return Task.CompletedTask;
        }
    }
}