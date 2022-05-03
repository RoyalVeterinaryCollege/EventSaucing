using Akka.Actor;
using Akka.DependencyInjection;
using EventSaucing.StreamProcessors;
using ExampleApp.OrderCounting;

namespace ExampleApp.Services;

public class StreamProcessorPropsProvider : IStreamProcessorInitialisation {
    private readonly ActorSystem _system;

    public StreamProcessorPropsProvider(ActorSystem system) {
        _system = system;
    }

    public IEnumerable<Props> GetReplicaScopedStreamProcessorProps() {
        return new List<Props>() { DependencyResolver.For(_system).Props<OrderCountingStreamProcessor>() };
    }

    public IEnumerable<ClusterStreamProcessorInitialisation> GetClusterScopedStreamProcessorsInitialisationParameters() {
        return new List<Props> { DependencyResolver.For(_system).Props<ItemCountingClusterStreamProcessor>() }.Select(x=> new ClusterStreamProcessorInitialisation(x, "Cluster"));
    }
}