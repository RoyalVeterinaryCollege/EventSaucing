using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DependencyInjection;
using EventSaucing.Projectors;
using EventSaucing.Reactors;
using EventSaucing.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices {

    //todo rename Projectors to StreamProcessors
    /// <summary>
    /// Starts and stops <see cref="ReplicaProjectorSupervisor"/> which supervises the dependency graph of projectors for this node.
    /// </summary>
    public class ProjectorService : IHostedService
    {
        private readonly IDbService _dbService;
        private readonly ActorSystem _actorSystem;
        private readonly ILogger<ProjectorService> _logger;
        private readonly IProjectorTypeProvider _projectorTypeProvider;
        private IActorRef _replicaProjectorSupervisor;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="dbService"></param>
        /// <param name="actorSystem"></param>
        /// <param name="logger"></param>
        /// <param name="projectorTypeProvider"></param>
        public ProjectorService(IDbService dbService, ActorSystem actorSystem, ILogger<ProjectorService> logger, IProjectorTypeProvider projectorTypeProvider)
        {
            _dbService = dbService;
            _actorSystem = actorSystem;
            _logger = logger;
            _projectorTypeProvider = projectorTypeProvider;
        }

        /// <summary>
        /// Starts <see cref="ReplicaProjectorSupervisor"/>
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StartAsync(CancellationToken cancellationToken)  {
            _logger.LogInformation($"EventSaucing {nameof(ProjectorService)} starting");

            // Ensure the Projector Status table is created.
            ProjectorHelper.InitialiseProjectorStatusStore(_dbService);

            // function to create the replica projectors, ctor dependency of ProjectorSupervisor
            Func<IUntypedActorContext, IEnumerable<IActorRef>> pollerMaker = (ctx) => {
                IEnumerable<Props> props= _projectorTypeProvider
                    .GetReplicaProjectorTypes()
                    .Select(type => DependencyResolver.For(_actorSystem).Props(type));
                return props.Select(prop => ctx.ActorOf(prop));
            };

            // start replica projector supervisor
            _replicaProjectorSupervisor = _actorSystem.ActorOf(Props.Create<ReplicaProjectorSupervisor>(pollerMaker));

            _logger.LogInformation($"EventSaucing {nameof(ProjectorService)} started");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops <see cref="ReplicaProjectorSupervisor"/>
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken){
            _logger.LogInformation($"EventSaucing {nameof(ProjectorService)} stop requested. Sending PoisonPill to {nameof(ReplicaProjectorSupervisor)} @ path {_replicaProjectorSupervisor.Path}");

            // from https://petabridge.com/blog/how-to-stop-an-actor-akkadotnet/
            // targetActorRef is sent a PoisonPill by default
            // and returns a task whose result confirms shutdown within 5 seconds
            var shutdown = _replicaProjectorSupervisor.GracefulStop(TimeSpan.FromSeconds(5));

            return shutdown;
        }
    }
}