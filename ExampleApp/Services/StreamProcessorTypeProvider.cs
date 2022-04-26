using Akka.Actor;
using Akka.DependencyInjection;
using EventSaucing.StreamProcessors;
using ExampleApp.OrderCounting;

namespace ExampleApp.Services;

public class StreamProcessorPropsProvider : IStreamProcessorPropsProvider {
    private readonly ActorSystem _system;

    public StreamProcessorPropsProvider(ActorSystem system) {
        _system = system;
    }

    public IEnumerable<Props> GetReplicaScopedStreamProcessorsProps() {

        return new List<Props>() { DependencyResolver.For(_system).Props<OrderCountingStreamProcessor>()};
    }

    public IEnumerable<Props> GetClusterScopedStreamProcessorsProps() {
        return new List<Props>{ DependencyResolver.For(_system).Props<ItemCountingClusterStreamProcessor>()};
    }
}