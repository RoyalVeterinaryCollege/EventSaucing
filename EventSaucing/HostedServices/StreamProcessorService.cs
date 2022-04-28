﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using EventSaucing.Storage;
using EventSaucing.StreamProcessors;
using EventSaucing.StreamProcessors.Projectors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scalesque;

namespace EventSaucing.HostedServices {
    /// <summary>
    /// Starts <see cref="StreamProcessor"/> actors for both replica-scope and cluster wide-scope <see cref="StreamProcessorSupervisor"/>.
    ///
    /// This is optional,  you must register this yourself if you intend to use <see cref="StreamProcessor"/> actors
    /// </summary>
    public class StreamProcessorService : IHostedService {
        private readonly IDbService _dbService;
        private readonly ActorSystem _actorSystem;
        private readonly ILogger<StreamProcessorService> _logger;
        private readonly IStreamProcessorPropsProvider _streamProcessorPropsProvider;

        /// <summary>
        /// Optional Actor of type <see cref="StreamProcessorSupervisor"/> which manages the replica-scoped <see cref="StreamProcessor"/> actors
        /// </summary>
        private Option<IActorRef> _replicaStreamProcessorSupervisor = Option.None();

        /// <summary>
        /// Optional Proxy to actor of type <see cref="StreamProcessorSupervisor"/>, which is a cluster singleton which manages the cluster-scoped <see cref="StreamProcessor"/> actors
        /// </summary>
        private Option<IActorRef> _clusterProjectorSupervisor = Option.None();

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="dbService"></param>
        /// <param name="actorSystem"></param>
        /// <param name="logger"></param>
        /// <param name="streamProcessorPropsProvider"></param>
        public StreamProcessorService(IDbService dbService, ActorSystem actorSystem,
            ILogger<StreamProcessorService> logger, IStreamProcessorPropsProvider streamProcessorPropsProvider) {
            _dbService = dbService;
            _actorSystem = actorSystem;
            _logger = logger;
            _streamProcessorPropsProvider = streamProcessorPropsProvider;
        }

        /// <summary>
        /// Creates a factory function for actors from the types
        /// </summary>
        /// <param name="streamProcessorTypes"></param>
        /// <returns></returns>
        private Props CreateSupervisorProps(IEnumerable<Props> streamProcessorTypes) {
            Func<IUntypedActorContext, IEnumerable<IActorRef>> func;
            func = ctx =>
                streamProcessorTypes.Select(props => ctx.ActorOf(props));
            return Props.Create<StreamProcessorSupervisor>(func);
        }

        /// <summary>
        /// Starts <see cref="StreamProcessorSupervisor"/>
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} starting");

            // start projector supervisor(s) for both replica scoped StreamProcessors and clusters scoped StreamProcessors
            var replicaScopedStreamProcessorsProps = _streamProcessorPropsProvider.GetReplicaScopedStreamProcessorsProps().ToList();

            if (replicaScopedStreamProcessorsProps.Any()) {
                // Ensure the StreamProcessor checkpoint table is created in the replica db
                // nb this is the data structure expected by SqlProjector, not LegacyProjector as we shouldn't be creating LeagacyProjectors in future
                using (var dbConnection = _dbService.GetReplica()) {
                    await ProjectorHelper.InitialiseProjectorStatusStore(dbConnection);
                }

                _replicaStreamProcessorSupervisor =_actorSystem.ActorOf(CreateSupervisorProps(replicaScopedStreamProcessorsProps)).ToSome();
                _logger.LogInformation($"EventSaucing started supervision of replica-scoped StreamProcessors of {string.Join(", ", replicaScopedStreamProcessorsProps.Select(x => x.TypeName))}");
            }


            var clusterScopedStreamProcessorProps = _streamProcessorPropsProvider.GetClusterScopedStreamProcessorsProps().ToList();

            if (clusterScopedStreamProcessorProps.Any()) {
                // Ensure the StreamProcessor checkpoint table is created in the cluster db
                // nb this is the data structure expected by SqlProjector, not LegacyProjector as we shouldn't be creating LeagacyProjectors in future
                using (var dbConnection = _dbService.GetCluster()) {
                    await ProjectorHelper.InitialiseProjectorStatusStore(dbConnection);
                }

                _clusterProjectorSupervisor =  _actorSystem.ActorOf(ClusterSingletonManager.Props(
                        singletonProps: CreateSupervisorProps(clusterScopedStreamProcessorProps),
                        terminationMessage: PoisonPill.Instance,
                        settings: ClusterSingletonManagerSettings.Create(_actorSystem).WithRole("api")),
                    name: "streamprocessor-supervisor").ToSome();

                _logger.LogInformation($"EventSaucing started supervision of cluster-scoped StreamProcessors of {string.Join(", ", clusterScopedStreamProcessorProps.Select(x => x.TypeName))}");

            }

            _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} started");
        }

        /// <summary>
        /// Stops replica-scoped <see cref="StreamProcessorSupervisor"/>
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} stop requested");

            if (_replicaStreamProcessorSupervisor.HasValue) {

                _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} stopping replica scoped {nameof(StreamProcessorSupervisor)}"); 
                //send stop which stops the actor as soon as it has finished processing the current message //https://petabridge.com/blog/how-to-stop-an-actor-akkadotnet/
                var actorRef = _replicaStreamProcessorSupervisor.Get();
                _actorSystem.Stop(actorRef);  
            }

            // i don't think we should shut the cluster-scoped supervisor down, as we don't actually know that the whole cluster is being stopped at this point
            // it might just be our node, let Akka handle shutting down the cluster singleton
            return Task.CompletedTask;
        }
    }
}