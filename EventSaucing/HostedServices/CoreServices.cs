using System;
using System.Threading;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.DependencyInjection;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices
{
    /// <summary>
    /// Starts EventSaucing core services.  You mist host this service for EventSaucing to work.  
    /// 
    /// Starts and stops Akka and also <see cref="LocalEventStreamActor"/> which produces a stream of serialised (in-order) commits for local and cluster usage
    /// </summary>
    public class CoreServices : IHostedService {
        private ActorSystem _actorSystem;
        private readonly IServiceProvider _sp;
        private readonly EventSaucingConfiguration _config;
        private readonly PostCommitNotifierPipeline _commitNotifierPipeline;
        private readonly ILogger<CoreServices> _logger;
        private readonly IInMemoryCommitSerialiserCache _cache;
        private IActorRef _localEventStreamActor;

        /// <summary>
        /// Instantiates
        /// </summary>
        public CoreServices(IServiceProvider sp, EventSaucingConfiguration config, ILogger<CoreServices> logger, PostCommitNotifierPipeline commitNotifierPipeline, IInMemoryCommitSerialiserCache cache) {
            _sp = sp;
            _config = config;
            _commitNotifierPipeline = commitNotifierPipeline;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// Starts core services
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(CoreServices)} starting");

            // start Akka
            // from https://getakka.net/articles/actors/dependency-injection.html

            // todo load HCONFIG from file
            // var hocon = ConfigurationFactory.ParseString(File.ReadAllText("app.conf"));
            var bootstrap = BootstrapSetup.Create();
            var di = DependencyResolverSetup.Create(_sp);
            var actorSystemSetup = bootstrap.And(di);
            _actorSystem = ActorSystem.Create(_config.ActorSystemName, actorSystemSetup);

            // function to create the EventStorePollerActor, ctor dependency of LocalEventStreamActor
            Func<IUntypedActorContext, IActorRef> pollerMaker = (ctx) => {
                    var pollerProps = DependencyResolver
                        .For(_actorSystem)
                        .Props<EventStorePollerActor>()
                        .WithSupervisorStrategy(SupervisorStrategy.StoppingStrategy);

                    return ctx.ActorOf(pollerProps);
                };

            // start the local event stream actor
            var streamerProps = DependencyResolver
                .For(_actorSystem)
                .Props<LocalEventStreamActor>(_cache, pollerMaker); //todo, im pretty sure we don't need to inject _cache here, just pollerMaker
            _localEventStreamActor = _actorSystem.ActorOf(streamerProps);

            //subscribe actor to distributed commit notification messages
            var mediator = DistributedPubSub.Get(_actorSystem).Mediator;
            mediator.Tell(new Subscribe(LocalEventStreamActor.PubSubCommitNotificationTopic, _localEventStreamActor));

            // watch for commits and publish them
            _commitNotifierPipeline.AfterCommit += CommitNotifierPipeline_AfterCommit;

            _logger.LogInformation($"EventSaucing {nameof(CoreServices)} started");

            return Task.CompletedTask;
        }
        /// <summary>
        /// Publish all local commits to all nodes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CommitNotifierPipeline_AfterCommit(object sender, global::NEventStore.ICommit e) {
            var msg = new CommitNotification(e);
            var mediator = DistributedPubSub.Get(_actorSystem).Mediator;
            mediator.Tell(new Publish(LocalEventStreamActor.PubSubCommitNotificationTopic, msg));
        }

        /// <summary>
        /// Stops core services including Akka
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(CoreServices)} stop requested.");
            _logger.LogInformation($"EventSaucing {nameof(CoreServices)} sending PoisonPill to {nameof(LocalEventStreamActor)} @ {_localEventStreamActor.Path}");
            _localEventStreamActor.Tell(PoisonPill.Instance, ActorRefs.NoSender);
            _logger.LogInformation("Akka is entering co-ordinated shutdown");
            Task<Done> shutdownTask = CoordinatedShutdown.Get(_actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
            return shutdownTask.ContinueWith(t => _logger.LogInformation("Akka is stopped"), cancellationToken);
        }
    }
}
