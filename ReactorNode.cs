using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DI.Core;
using Autofac;
using EventSaucing.Reactors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing
{
    /// <summary>
    /// Starts a local Reactor bucket which automatically connects to the main EventSaucing Reactor system.
    /// Use this for secondary Reactor processes outside of the main webserver. Assumes you have already called RegisterEventSaucingModules.
    /// </summary>
    public class ReactorNode  : IHostedService
    {
        private readonly ILifetimeScope _autofacContainer;
        private readonly ILogger<ReactorNode> _logger;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="autofacContainer"></param>
        /// <param name="logger"></param>
        public ReactorNode(ILifetimeScope autofacContainer, ILogger<ReactorNode> logger){
            _autofacContainer = autofacContainer;
            _logger = logger;
        }
        /// <summary>
        /// Starts a local reactor node
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("EventSaucing Reactor node starting");
            var actorSystem = _autofacContainer.Resolve<ActorSystem>();

            //start the local reactor bucket supervisor.  It will automatically connect to the main Reactor process.
            var bucket = actorSystem.ActorOf(actorSystem.DI().Props<ReactorBucketSupervisor>(), name: "reactor-bucket");
            // bucket.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket(localReactorBucketName));
            bucket.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket("testing ")); //todo .net 5 port, move to config

            return Task.CompletedTask;
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
