using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DI.Core;
using EventSaucing.Reactors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices
{
    /// <summary>
    /// Starts Reactor supervision for an akka cluster.
    ///
    /// Need to start this once per akka cluster on the main node.  You call this even if your cluster only has one node. Assumes you have already called RegisterEventSaucingModules.
    /// </summary>
    public class ReactorClusterSupervision : IHostedService  {
        private readonly IReactorRepository _reactorRepo;
        private readonly ActorSystem _actorSystem;
        private readonly ILogger<ReactorClusterSupervision> _logger;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="reactorRepo"></param>
        /// <param name="actorSystem"></param>
        /// <param name="logger"></param>
        public ReactorClusterSupervision(IReactorRepository reactorRepo, ActorSystem actorSystem, ILogger<ReactorClusterSupervision> logger) {
            _reactorRepo = reactorRepo;
            _actorSystem = actorSystem;
            _logger = logger;
        }
        /// <summary>
        /// Starts reactor cluster supervision
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("EventSaucing ReactorCluster Supervision starting");

            //create the reactor persistence tables if not already created
            await _reactorRepo.CreateReactorTablesAsync();

            //start the overall reactor infrastructure, only one of these needed per cluster
            var reactorsuper = _actorSystem.ActorOf(_actorSystem.DI().Props<ReactorSupervisor>(), name: "reactor-supervisor");

            //tell the local infrastructure its bucket identity
            reactorsuper.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket("CRIS 3"));//todo : get bucket name from config
            _logger.LogInformation("EventSaucing ReactorCluster Supervision started");
        }

        /// <summary>
        /// no-op
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("EventSaucing ReactorCluster Supervision stopped");
            return Task.CompletedTask;
        }
    }
}
