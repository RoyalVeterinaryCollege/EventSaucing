using System;
using System.Threading.Tasks;
using EventSaucing.StreamProcessors;

namespace EventSaucing.Projectors {
    public class FakeCheckpointPersister : IStreamProcessorCheckpointPersister {
        public Task<long> GetInitialCheckpointAsync(StreamProcessor streamProcessor) {
            return Task.FromResult(10L); // currently all tests are set up with SP at checkpoint 10
        }

        public Task PersistCheckpointAsync(StreamProcessor streamProcessor, long checkpoint) {
            return Task.CompletedTask;//todo this fake implementation might not work
        }
    }
}