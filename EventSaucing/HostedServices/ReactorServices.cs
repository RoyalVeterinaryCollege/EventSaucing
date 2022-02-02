using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DI.Core;
using EventSaucing.Reactors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices
{
    /// <summary>
    /// Starts a local Reactor node using the bucket set in config
    /// </summary>
    public class ReactorServices  : IHostedService
    {
        private readonly ActorSystem _actorSystem;
        private readonly ILogger<ReactorServices> _logger;
        private readonly IReactorRepository _reactorRepo;
        private readonly IConfiguration _config;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="dependencyResolver">Required.  If you remove this, then autofac starts this class before the actor system is configured to use DI and actors cant be created</param>
        /// <param name="logger"></param>
        /// <param name="reactorRepo"></param>
        /// <param name="config"></param>
        public ReactorServices(ActorSystem actorSystem, IDependencyResolver dependencyResolver, ILogger<ReactorServices> logger, IReactorRepository reactorRepo, IConfiguration config){
            _actorSystem = actorSystem;
            _logger = logger;
            _reactorRepo = reactorRepo;
            _config = config;
        }
        /// <summary>
        /// Starts a local reactor node
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("EventSaucing Reactor node starting");

            //create the reactor persistence tables if not already created
            await _reactorRepo.CreateReactorTablesAsync();

            //start the local reactor bucket supervisor.  It will automatically connect to the main Reactor process.
            _actorSystem.ActorOf(_actorSystem.DI().Props<ReactorBucketSupervisor>(), name: "reactor-bucket");

            //start the local royal mail.
            var actor = _actorSystem.ActorOf(_actorSystem.DI().Props<RoyalMail>(), "royal-mail");

            //schedule a poll message to be sent to RoyalMail every n seconds
            _actorSystem.Scheduler.ScheduleTellRepeatedly(
                TimeSpan.FromSeconds(_config.GetValue<int?>("EventSaucing:RoyalMail:StartupDelay") ?? 5), // on start up, wait this long before polling
                TimeSpan.FromSeconds(_config.GetValue<int?>("EventSaucing:RoyalMail:PollingInterval") ?? 5), // wait this long between polling
                actor, 
                new RoyalMail.LocalMessages.PollForOutstandingArticles(),
                ActorRefs.NoSender);
        }

        /// <summary>
        /// Stops a local reactor node
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("EventSaucing Reactor node stopping"); //no-op actor system stops itself
            return Task.CompletedTask;
        }
    }
}
