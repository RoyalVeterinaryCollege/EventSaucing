using System;
using System.Threading;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.DependencyInjection;
using Akka.Dispatch.SysMsg;
using Autofac;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices
{
    /// <summary>
    /// Starts EventSaucing event stream.  
    /// 
    /// Starts and stops <see cref="LocalEventStreamActor"/> which produces a stream of serialised (in-order) commits for local and cluster usage
    /// </summary>
    public class EventStreamService : IHostedService {
        private ActorSystem _actorSystem;
        private readonly PostCommitNotifierPipeline _commitNotifierPipeline;
        private readonly ILogger<EventStreamService> _logger;
        private readonly IInMemoryCommitSerialiserCache _cache;
        private IActorRef _localEventStreamActor;

        /// <summary>
        /// Instantiates
        /// </summary>
        public EventStreamService(ActorSystem actorSystem, ILogger<EventStreamService> logger, PostCommitNotifierPipeline commitNotifierPipeline, IInMemoryCommitSerialiserCache cache) {
            _actorSystem = actorSystem;
            _commitNotifierPipeline = commitNotifierPipeline;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// Starts LocalEventStreamActor
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(EventStreamService)} starting");
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
                .Props<LocalEventStreamActor>(pollerMaker); 
            _localEventStreamActor = _actorSystem.ActorOf(streamerProps);

            //subscribe actor to distributed commit notification messages
            var mediator = DistributedPubSub.Get(_actorSystem).Mediator;
            mediator.Tell(new Subscribe(LocalEventStreamActor.PubSubCommitNotificationTopic, _localEventStreamActor));

            // watch for commits and publish them
            _commitNotifierPipeline.AfterCommit += CommitNotifierPipeline_AfterCommit;

            _logger.LogInformation($"EventSaucing {nameof(EventStreamService)} started");

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
        /// Stops LocalEventStreamActor
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(EventStreamService)} stop requested.");
            _logger.LogInformation($"EventSaucing {nameof(EventStreamService)} sending Stop to {nameof(LocalEventStreamActor)} @ {_localEventStreamActor.Path}");
            return _localEventStreamActor
                .GracefulStop(TimeSpan.FromSeconds(5), new Stop())
                .ContinueWith(t => _logger.LogInformation($"{nameof(LocalEventStreamActor)} is stopped"), cancellationToken);
        }
    }
}
