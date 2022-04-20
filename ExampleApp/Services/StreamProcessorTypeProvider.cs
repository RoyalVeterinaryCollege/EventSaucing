using EventSaucing.StreamProcessors;
using ExampleApp.OrderCounting;

namespace ExampleApp.Services;

public class StreamProcessorTypeProvider : IStreamProcessorTypeProvider {
    public IEnumerable<Type> GetReplicaScopedStreamProcessorsTypes() {
        return new List<Type>(){typeof(OrderCountingStreamProcessor)};
    }

    public IEnumerable<Type> GetClusterScopedStreamProcessorsTypes() {
        return new List<Type>() { typeof(ItemCountingClusterStreamProcessor) };
    }
}