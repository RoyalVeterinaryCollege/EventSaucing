﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.DependencyInjection;
using Akka.Routing;
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
        private IActorRef _royalMailActor;
        private IActorRef _reactorBucket;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="logger"></param>
        /// <param name="reactorRepo"></param>
        /// <param name="config"></param>
        public ReactorServices(ActorSystem actorSystem, ILogger<ReactorServices> logger, IReactorRepository reactorRepo, IConfiguration config){
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

            // start the local reactor bucket supervisor.  
            // todo convert ReactorBucketSupervisor to sharded actor
            
            var reactorBucketProps = DependencyResolver.For(_actorSystem).Props<ReactorBucketSupervisor>();
            _reactorBucket = _actorSystem.ActorOf(reactorBucketProps, "reactor-bucket");

            // register actor type as a sharded entity
            // todo: do we even need the reactor supervisor anymore? cant messages just go to reactoractor directly?
            var region = await ClusterSharding.Get(_actorSystem).StartAsync(
                typeName: "reactor-bucket",
                entityPropsFactory: entityId => Props.Create(() => new ReactorBucketSupervisor(entityId, _config)),
                settings: ClusterShardingSettings.Create(_actorSystem),
                messageExtractor: new MessageExtractor());

            // send message to entity through shard region
            region.Tell(new ShardEnvelope(shardId: 1, entityId: 1, message: "hello"));

            // start the RoyalMail as a cluster singleton https://getakka.net/articles/clustering/cluster-singleton.html
            // this means there is only one per cluster (with various caveats, that don't matter too much for RoyalMail)
            _royalMailActor = _actorSystem.ActorOf(
                ClusterSingletonManager.Props(
                    singletonProps: Props.Create<RoyalMail>(),
                    terminationMessage: PoisonPill.Instance,
                    settings: ClusterSingletonManagerSettings.Create(_actorSystem).WithRole("reactors")), //todo : configure cluster roles
                name: "royal-mail");
        }

        /// <summary>
        /// Stops a local reactor node
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("EventSaucing Reactor node stopping"); 
            _royalMailActor.Tell(PoisonPill.Instance);
            _reactorBucket.Tell(PoisonPill.Instance);
            return Task.CompletedTask;
        }
    }
}
