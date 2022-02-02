using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DI.Core;
using EventSaucing.Projectors;
using EventSaucing.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices {

    //todo check this all once the code is complete

    /// <summary>
    /// Starts and stops <see cref="ProjectorSupervisor"/> which supervises the dependency graph of projectors for this node.
    /// </summary>
    public class ProjectorServices : IHostedService
    {
        private readonly IDbService _dbService;
        private readonly ActorSystem _actorSystem;
        private readonly ILogger<ProjectorServices> _logger;
        private IActorRef _localProjectorSupervisor;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="dbService"></param>
        /// <param name="actorSystem"></param>
        /// <param name="dependencyResolver">Required.  If you remove this, then autofac starts this class before the actor system is configured to use DI and actors cant be created</param>
        /// <param name="logger"></param>
        public ProjectorServices(IDbService dbService, ActorSystem actorSystem, IDependencyResolver dependencyResolver,ILogger<ProjectorServices> logger)
        {
            _dbService = dbService;
            _actorSystem = actorSystem;
            _logger = logger;
        }

        /// <summary>
        /// Starts the pipeline
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"EventSaucing {nameof(ProjectorServices)} starting");

            // Ensure the Projector Status table is created.
            ProjectorHelper.InitialiseProjectorStatusStore(_dbService);
            _localProjectorSupervisor = _actorSystem.ActorOf(_actorSystem.DI().Props<ProjectorSupervisor>(), nameof(ProjectorSupervisor));
            _logger.LogInformation($"EventSaucing {nameof(ProjectorServices)} started");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops the local projector supervisor
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken){
            _logger.LogInformation($"EventSaucing {nameof(ProjectorServices)} stop requested. Sending PoisonPill to {nameof(ProjectorSupervisor)} @ path {_localProjectorSupervisor.Path}");
            _localProjectorSupervisor.Tell(PoisonPill.Instance, ActorRefs.NoSender);
            return Task.CompletedTask;
        }
    }
}