﻿using Akka.Actor;
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

    public IEnumerable<ClusterStreamProcessorInitialisation> GetReplicaScopedStreamProcessorProps() {
        var replicaScopedProcessors = new List<ClusterStreamProcessorInitialisation> {
            GetIoC<OrderCountingStreamProcessor>()
        };

        return replicaScopedProcessors;
    }

    public IEnumerable<ClusterStreamProcessorInitialisation> GetClusterScopedStreamProcessorsInitialisationParameters() {
        var clusterRole = "api".ToSome();

        var clusterScopedStreamProcessors = new List<ClusterStreamProcessorInitialisation> {
            GetIoC<ItemCountingClusterStreamProcessor>(clusterRole)
        };

        return clusterScopedStreamProcessors;
    }

    /// <summary>
    /// Gets ClusterStreamProcessorInitialisation with IoC ctor parameters only
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    private ClusterStreamProcessorInitialisation GetIoC<T>() => GetIoC<T>(Option.None());

    /// <summary>
    /// Gets ClusterStreamProcessorInitialisation with IoC ctor parameters only
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="clusterRole"></param>
    /// <returns></returns>
    private ClusterStreamProcessorInitialisation GetIoC<T>(Option<string> clusterRole) =>
        new(
            props: DependencyResolver
                .For(_system)
                .Props(typeof(T)),
            actorName: typeof(T).FullName,
            clusterRole: clusterRole
        );
}
