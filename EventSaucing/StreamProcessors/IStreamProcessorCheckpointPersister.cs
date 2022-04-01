using System.Threading.Tasks;
using Scalesque;

namespace EventSaucing.StreamProcessors
{
    public interface IStreamProcessorCheckpointPersister<T> {
        Task<long> GetCommitStoreHeadAsync();
        Task<Option<long>> GetCheckpointAsync(T streamProcessorId);
        Task PersistCheckpointAsync(T streamProcessorId, long checkpoint);
    }
}
