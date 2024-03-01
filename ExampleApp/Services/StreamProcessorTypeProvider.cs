using Akka.Actor;
using Akka.DependencyInjection;
using EventSaucing.StreamProcessors;
using ExampleApp.OrderCounting;
using Scalesque;

namespace ExampleApp.Services;

public class StreamProcessorPropsProvider : IStreamProcessorInitialisation {
    private readonly ActorSystem _system;

    public StreamProcessorPropsProvider(ActorSystem system) {
        _system = system;
    }

    public IEnumerable<ReplicaStreamProcessorInitialisation> GetReplicaScopedStreamProcessorProps() {
        var replicaScopedProcessors = new List<ReplicaStreamProcessorInitialisation> {
            GetReplicaStreamProcessor<OrderCountingStreamProcessor>(),
            GetReplicaStreamProcessor<ErrorThrowingStreamProcessor>()
        };

        return replicaScopedProcessors;
    }

    public IEnumerable<ClusterStreamProcessorInitialisation> GetClusterScopedStreamProcessorsInitialisationParameters() {
        var clusterRole = "api";

        var clusterScopedStreamProcessors = new List<ClusterStreamProcessorInitialisation> {
            GetClusterStreamProcessor<ItemCountingClusterStreamProcessor>(clusterRole)
        };

        return clusterScopedStreamProcessors;
    }

    /// <summary>
    /// Gets ClusterStreamProcessorInitialisation with IoC ctor parameters only
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="clusterRole"></param>
    /// <returns></returns>
    private ClusterStreamProcessorInitialisation GetClusterStreamProcessor<T>(string clusterRole) =>
        new(
            props: DependencyResolver
                .For(_system)
                .Props(typeof(T)),
            actorName: typeof(T).FullName,
            clusterRole: clusterRole
        );

    private ReplicaStreamProcessorInitialisation GetReplicaStreamProcessor<T>() =>
        new(
            props: DependencyResolver
                .For(_system)
                .Props(typeof(T)),
            actorName: typeof(T).FullName
        );
}
