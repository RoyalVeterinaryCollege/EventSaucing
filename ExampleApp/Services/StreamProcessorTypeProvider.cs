using EventSaucing.StreamProcessors;

namespace ExampleApp.Services;

public class StreamProcessorTypeProvider : IStreamProcessorTypeProvider {
    public IEnumerable<Type> GetReplicaScopedStreamProcessorsTypes() {
        return new List<Type>();
    }

    public IEnumerable<Type> GetClusterScopedStreamProcessorsTypes() {
        return new List<Type>();
    }
}