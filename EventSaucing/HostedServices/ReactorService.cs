using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.DependencyInjection;
using EventSaucing.Reactors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices
{
    /// <summary>
    /// Starts a local Reactor node using the bucket set in config
    /// </summary>
    public class ReactorService  : IHostedService
    {
        private readonly ActorSystem _actorSystem;
        private readonly ILogger<ReactorService> _logger;
        private readonly IReactorRepository _reactorRepo;
        private IActorRef _royalMailActor;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="logger"></param>
        /// <param name="reactorRepo"></param>
        public ReactorService(ActorSystem actorSystem, ILogger<ReactorService> logger, IReactorRepository reactorRepo){
            _actorSystem = actorSystem;
            _logger = logger;
            _reactorRepo = reactorRepo;
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
            // todo convert ReactorBucketSupervisor to sharded actor or delete it

            var dependencyResolver = DependencyResolver.For(_actorSystem);

            //var reactorBucketProps = DependencyResolver.For(_actorSystem).Props<ReactorBucketSupervisor>();
            //_reactorBucket = _actorSystem.ActorOf(reactorBucketProps, "reactor-bucket");

            // register actor type as a sharded entity
            // todo: do we even need the reactor supervisor anymore? cant messages just go to reactoractor directly?
            var region = await ClusterSharding.Get(_actorSystem).StartAsync(
                typeName: "reactor-actor",
                settings: ClusterShardingSettings.Create(_actorSystem).WithRole("reactors-{bucket}"), //todo cluster roles
                entityPropsFactory: entityId => dependencyResolver.Props<ReactorActor>(entityId),
                messageExtractor: new ReactorMessageExtractor(30) //todo num shards from config  As a rule of thumb, you may decide to have a number of shards ten times greater than expected maximum number of cluster nodes.
                ); 

            // start the RoyalMail as a cluster singleton https://getakka.net/articles/clustering/cluster-singleton.html
            // this means there is only one per cluster (the caveats don't matter too much for RoyalMail)
            _royalMailActor = _actorSystem.ActorOf(
                ClusterSingletonManager.Props(
                    singletonProps: dependencyResolver.Props<RoyalMail>(),
                    terminationMessage: PoisonPill.Instance,
                    settings: ClusterSingletonManagerSettings.Create(_actorSystem).WithRole(AkkaRoles.Reactors)), //todo cluster roles
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
            //i dont believe we should stop the shard actor ourselves
            return Task.CompletedTask;
        }
    }
}
