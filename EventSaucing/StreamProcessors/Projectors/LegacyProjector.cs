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
        public int ProjectorId { get; }

        /// <summary>
        /// Instantiates
        /// </summary>
        public LegacyProjector(IPersistStreams persistStreams, IStreamProcessorCheckpointPersister checkpointPersister) :base(persistStreams, checkpointPersister) {
            ProjectorId = this.GetProjectorId();
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