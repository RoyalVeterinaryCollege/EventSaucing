using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.Storage;
using Microsoft.Extensions.Configuration;
using NEventStore;
using NEventStore.Persistence;
using Scalesque;

namespace EventSaucing.StreamProcessors.Projectors {

    /// <summary>
    /// Obsolete replacement for ProjectorBase.  Provided for backwards compatibility only.  Prefer <see cref="SqlProjector"/> for future usage.
    /// </summary>
    [Obsolete("Provided for backwards compatibility only.  Prefer SqlProjector for future usage")]
    public abstract class LegacyProjector : StreamProcessor {
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
        public LegacyProjector(IPersistStreams persistStreams, IDbService dbService, IConfiguration config):base(persistStreams) {
            _persistStreams = persistStreams;
            _dbService = dbService;
            ProjectorId = this.GetProjectorId();

            var initialiseAtHead = config.GetSection("EventSaucing:Projectors:InitialiseAtHead").Get<string[]>();
            _initialiseAtHead = initialiseAtHead.Contains(GetType().FullName);
        }

        protected override void PreStart() {
            //get the persisted checkpoint (if there is one)
            using (var conn = _dbService.GetReadmodel()) {
                conn.Open();

                Option<long> results =
                    conn.Query<long>(
                        "SELECT LastCheckPointToken FROM dbo.ProjectorStatus WHERE ProjectorId = @ProjectorId",
                        new { this.ProjectorId }).HeadOption();

                if (results.HasValue) {
                    InitialCheckpoint = results;  // if we have a persisted checkpoint, use as initial checkpoint
                } else if (_initialiseAtHead) {
                    InitialCheckpoint = conn.ExecuteScalar<long>("SELECT MAX(CheckpointNumber) FROM dbo.Commits").ToSome(); // or initialise at head if requested
                }
            }

            base.PreStart();
        }

        protected override async Task PersistCheckpointAsync()  {
            using (var conn = _dbService.GetReadmodel()) {
                await conn.OpenAsync();
                this.PersistProjectorCheckpoint(conn);
            }
        }

        /// <summary>
        /// Projects the commit by delegating it to the synchronous Project method
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>Task</returns>
        public override Task<bool> ProjectAsync(ICommit commit) {
            return Task.FromResult(Project(commit));
        }

        /// <summary>
        /// Projects the commit synchronously
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>Bool True if projection of a readmodel occurred.  False if the projector didn't project any events in the ICommit</returns>
        public abstract bool Project(ICommit commit);
    }
}