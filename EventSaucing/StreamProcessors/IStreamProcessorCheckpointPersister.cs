using System.Threading.Tasks;

namespace EventSaucing.StreamProcessors {
    /// <summary>
    /// A service which deals with persistence of StreamProcessor checkpoint state
    /// </summary>
    public interface IStreamProcessorCheckpointPersister {
        /// <summary>
        /// Gets a previously persisted Checkpoint state from the db.  To initialise the StreamProcessor at the first commit, return 0L
        /// </summary>
        /// <param name="streamProcessor"></param>
        /// <returns></returns>
        Task<long> GetInitialCheckpointAsync(StreamProcessor streamProcessor);
        /// <summary>
        /// Persists the StreamProcessor's checkpoint in the db.
        /// </summary>
        /// <param name="streamProcessor"></param>
        /// <param name="checkpoint"></param>
        /// <returns></returns>
        Task PersistCheckpointAsync(StreamProcessor streamProcessor, long checkpoint);
    }
}