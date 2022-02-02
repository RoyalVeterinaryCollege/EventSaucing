using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.DI.Core;
using EventSaucing.DependencyInjection.Autofac;
using EventSaucing.NEventStore;
using EventSaucing.Projectors;
using EventSaucing.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices
{
    /// <summary>
    ///  Starts the EventSaucing projector pipeline.
    /// </summary>
    public class ProjectorPipeline : IHostedService {
        private readonly IDbService _dbService;
        private readonly ActorSystem _actorSystem;
        private readonly PostCommitNotifierPipeline _commitNotifierPipeline;
        private readonly ILogger<ProjectorPipeline> _logger;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="dbService"></param>
        /// <param name="actorSystem"></param>
        /// <param name="commitNotifierPipeline"></param>
        /// <param name="logger"></param>
        public ProjectorPipeline(IDbService dbService, ActorSystem actorSystem, PostCommitNotifierPipeline commitNotifierPipeline, ILogger<ProjectorPipeline> logger) {
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
            _logger.LogInformation("EventSaucing ProjectorPipeline starting");

            // Ensure the Projector Status is initialised.
            ProjectorHelper.InitialiseProjectorStatusStore(_dbService);

            // start the local event stream actor
            var actor = _actorSystem.ActorOf(_actorSystem.DI().Props<LocalEventStreamActor>(), nameof(LocalEventStreamActor));

            _commitNotifierPipeline.AfterCommit += CommitNotifierPipeline_AfterCommit;

            _logger.LogInformation("EventSaucing ProjectorPipeline started");

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
        /// no-op
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("EventSaucing ProjectorPipeline stop requested");
            return Task.CompletedTask;
        }
    }
}
