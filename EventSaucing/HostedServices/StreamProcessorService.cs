using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.DependencyInjection;
using EventSaucing.Storage;
using EventSaucing.StreamProcessors;
using EventSaucing.StreamProcessors.Projectors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices {
    /// <summary>
    /// Starts and stops <see cref="StreamProcessorSupervisor"/> which supervises the projectors and reactors for this node.
    /// </summary>
    public class StreamProcessorService : IHostedService {
        private readonly IDbService _dbService;
        private readonly ActorSystem _actorSystem;
        private readonly ILogger<StreamProcessorService> _logger;
        private readonly IStreamProcessorTypeProvider _streamProcessorTypeProvider;
        private IActorRef _replicaProjectorSupervisor;


        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="dbService"></param>
        /// <param name="actorSystem"></param>
        /// <param name="logger"></param>
        /// <param name="streamProcessorTypeProvider"></param>
        public StreamProcessorService(IDbService dbService, ActorSystem actorSystem,
            ILogger<StreamProcessorService> logger, IStreamProcessorTypeProvider streamProcessorTypeProvider) {
            _dbService = dbService;
            _actorSystem = actorSystem;
            _logger = logger;
            _streamProcessorTypeProvider = streamProcessorTypeProvider;
        }

        /// <summary>
        /// Creates a factory function for actors from the types
        /// </summary>
        /// <param name="streamProcessorTypes"></param>
        /// <returns></returns>
        private Props CreateSupervisorProps(IEnumerable<Type> streamProcessorTypes) {
            Func<IUntypedActorContext, IEnumerable<IActorRef>> func;
            func = ctx =>
                streamProcessorTypes.Select(type => DependencyResolver.For(_actorSystem).Props(type))
                    .Select(props => ctx.ActorOf(props));
            return Props.Create<StreamProcessorSupervisor>(func);
        }


        /// <summary>
        /// Starts <see cref="StreamProcessorSupervisor"/>
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} starting");

            // Ensure the Projector Status table is created in the replica db
            ProjectorHelper
                .InitialiseProjectorStatusStore(_dbService); //todo rename projector status to stream processor status

            // start replica projector supervisor
            _replicaProjectorSupervisor =
                _actorSystem.ActorOf(CreateSupervisorProps(_streamProcessorTypeProvider.GetProjectorTypes()));

            var reactors = _streamProcessorTypeProvider.GetReactorTypes().ToList();

            if (reactors.Any()) {
                // todo : need to create streamprocessor checkpoint tables in the cluster wide db 
                _actorSystem.ActorOf(ClusterSingletonManager.Props(
                        singletonProps: CreateSupervisorProps(reactors),
                        terminationMessage: PoisonPill.Instance,
                        settings: ClusterSingletonManagerSettings.Create(_actorSystem).WithRole("api")),
                    name: "reactor-supervisor");

                _logger.LogInformation($"EventSaucing reactor supervision started");
            }

            _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} started");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops <see cref="StreamProcessorSupervisor"/>
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation(
                $"EventSaucing {nameof(StreamProcessorService)} stop requested. Sending PoisonPill to local replica projectors @ path {_replicaProjectorSupervisor.Path}");

            // from https://petabridge.com/blog/how-to-stop-an-actor-akkadotnet/
            // targetActorRef is sent a PoisonPill by default
            // and returns a task whose result confirms shutdown within 5 seconds
            var shutdown = _replicaProjectorSupervisor.GracefulStop(TimeSpan.FromSeconds(5));

            // i don't think we should shut the reactors down, as they might be about to migrate to another server

            return shutdown;
        }
    }
}