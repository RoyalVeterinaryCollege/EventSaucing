﻿using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.DI.Core;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using EventSaucing.Projectors;
using EventSaucing.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices
{
    /// <summary>
    /// Starts EventSaucing core services.  This is required for a node to participate in a cluster.
    ///
    /// Starts <see cref="LocalEventStreamActor"/> which produces a stream of serialised (in-order) commits for local downstream usage
    /// </summary>
    public class CoreServices : IHostedService {
        private readonly IDbService _dbService;
        private readonly ActorSystem _actorSystem;
        private readonly PostCommitNotifierPipeline _commitNotifierPipeline;
        private readonly ILogger<CoreServices> _logger;
        private IActorRef _localEventStreamActor;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="dbService"></param>
        /// <param name="dependencyResolver">Required.  If you remove this, then autofac starts this class before the actor system is configured to use DI and actors cant be created</param>
        /// <param name="actorSystem"></param>
        /// <param name="commitNotifierPipeline"></param>
        /// <param name="logger"></param>
        public CoreServices(IDbService dbService, IDependencyResolver dependencyResolver, ActorSystem actorSystem, PostCommitNotifierPipeline commitNotifierPipeline, ILogger<CoreServices> logger) {
            _dbService = dbService;
            _actorSystem = actorSystem;
            _commitNotifierPipeline = commitNotifierPipeline;
            _logger = logger;
        }

        /// <summary>
        /// Starts the pipeline
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(CoreServices)} starting");

            // start the local event stream actor
            _localEventStreamActor = _actorSystem.ActorOf(_actorSystem.DI().Props<LocalEventStreamActor>(), nameof(LocalEventStreamActor));

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
        /// Stops core services
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(CoreServices)} stop requested. Sending PoisonPill to {nameof(LocalEventStreamActor)} @ {_localEventStreamActor.Path}");
            _localEventStreamActor.Tell(PoisonPill.Instance, ActorRefs.NoSender);
            return Task.CompletedTask;
        }
    }
}