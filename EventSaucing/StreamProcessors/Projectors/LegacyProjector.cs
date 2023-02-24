using System;
using System.Threading.Tasks;
using NEventStore;
using NEventStore.Persistence;

namespace EventSaucing.StreamProcessors.Projectors {

    /// <summary>
    /// Obsolete replacement for ProjectorBase.  Provided for backwards compatibility only.  Prefer <see cref="SqlProjector"/> for future projectors.
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
        public override Task<bool> ProcessAsync(ICommit commit) {
            return Task.FromResult(Project(commit));
        }

        /// <summary>
        /// Projects the commit synchronously
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>>Bool True if checkpoint should be persisted</returns>
        public abstract bool Project(ICommit commit);
    }
}