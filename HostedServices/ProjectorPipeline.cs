using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DI.Core;
using EventSaucing.Akka.Actors;
using EventSaucing.DependencyInjection.Autofac;
using EventSaucing.Projector;
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
        private readonly ActorPaths _actorPaths;
        private readonly ILogger<ReactorClusterSupervision> _logger;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="dbService"></param>
        /// <param name="actorSystem"></param>
        /// <param name="actorPaths"></param>
        /// <param name="logger"></param>
        public ProjectorPipeline(IDbService dbService, ActorSystem actorSystem, ActorPaths actorPaths, ILogger<ReactorClusterSupervision> logger) {
            _dbService = dbService;
            _actorSystem = actorSystem;
            _actorPaths = actorPaths;
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

            //create the commit serialiser actor and set its path
            var commitSerialisor = _actorSystem.ActorOf(_actorSystem.DI().Props<CommitSerialiserActor>(), nameof(CommitSerialiserActor));
            _actorPaths.LocalCommitSerialisor = commitSerialisor.Path;
            _logger.LogInformation("EventSaucing ProjectorPipeline started");

            return Task.CompletedTask;
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
