using System.Data.Common;
using System.Threading.Tasks;

namespace EventSaucing.StreamProcessors {
    /// <summary>
    /// A service which deals with persistence of StreamProcessor checkpoint state
    /// </summary>
    public interface IStreamProcessorCheckpointPersister {
        /// <summary>
        /// Gets a previously persisted Checkpoint state from the db.  If StreamProcessor has never had a checkpoint persisted, return 0L
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