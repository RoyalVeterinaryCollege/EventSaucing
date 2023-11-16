using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Pattern;
using EventSaucing.Storage;
using EventSaucing.StreamProcessors;
using EventSaucing.StreamProcessors.Projectors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scalesque;

namespace EventSaucing.HostedServices {
    /// <summary>
    /// Starts <see cref="StreamProcessor"/> actors for both replica-scope and cluster wide-scope <see cref="StreamProcessorSupervisor"/>.
    ///
    /// This is optional,  you must register this yourself if you intend to use <see cref="StreamProcessor"/> actors
    /// </summary>
    public class StreamProcessorService : IHostedService {
        private readonly IDbService _dbService;
        private readonly ActorSystem _actorSystem;
        private readonly ILogger<StreamProcessorService> _logger;
        private readonly IStreamProcessorInitialisation _streamProcessorInitialisation;

        /// <summary>
        /// Optional Actor of type <see cref="StreamProcessorSupervisor"/> which manages the replica-scoped <see cref="StreamProcessor"/> actors
        /// </summary>
        private Option<IActorRef> _replicaStreamProcessorSupervisor = Option.None();

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="dbService"></param>
        /// <param name="actorSystem"></param>
        /// <param name="logger"></param>
        /// <param name="streamProcessorInitialisation"></param>
        public StreamProcessorService(IDbService dbService, ActorSystem actorSystem,
            ILogger<StreamProcessorService> logger, IStreamProcessorInitialisation streamProcessorInitialisation) {
            _dbService = dbService;
            _actorSystem = actorSystem;
            _logger = logger;
            _streamProcessorInitialisation = streamProcessorInitialisation;
        }

        /// <summary>
        /// Creates a factory function for actors from the types
        /// </summary>
        /// <param name="streamProcessorTypes"></param>
        /// <returns></returns>
        private Props CreateSupervisorProps(IEnumerable<ReplicaStreamProcessorInitialisation> streamProcessorTypes) {
            Func<IUntypedActorContext, IEnumerable<IActorRef>> func;
            
            func = ctx => streamProcessorTypes.Select(ix => {
                // create a back off supervisor which will start the given actor after it has stopped because of a failure,
                // in increasing intervals of 3, 6, 12, 24, 48 and finally 60 seconds:
                // this stops the actor from restarting too quickly and causing a large number of errors to be logged

                var backOffProps = BackoffSupervisor.Props(
                    Backoff.OnFailure(
                        ix.Props,
                        childName: ix.ActorName,
                        minBackoff: TimeSpan.FromSeconds(3),
                        maxBackoff: TimeSpan.FromSeconds(60),
                        randomFactor: 0.2,// adds 20% "noise" to vary the intervals slightly
                        maxNrOfRetries:-1));  // -1 = forever
                return ctx.ActorOf(backOffProps);
            });
            return Props.Create<StreamProcessorSupervisor>(func);
        }

        /// <summary>
        /// Starts <see cref="StreamProcessorSupervisor"/>
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} starting");

            // start projector supervisor(s) for both replica scoped StreamProcessors and clusters scoped StreamProcessors
            var replicaScopedStreamProcessorsProps = _streamProcessorInitialisation.GetReplicaScopedStreamProcessorProps().ToList();

            if (replicaScopedStreamProcessorsProps.Any()) {
                // Ensure the StreamProcessor checkpoint table is created in the replica db
                // nb this is the data structure expected by SqlProjector, not LegacyProjector as we shouldn't be creating LeagacyProjectors in future
                using (var dbConnection = _dbService.GetReplica()) {
                    await ProjectorHelper.InitialiseProjectorStatusStore(dbConnection);
                }

                _replicaStreamProcessorSupervisor =_actorSystem.ActorOf(CreateSupervisorProps(replicaScopedStreamProcessorsProps), "replica-scoped-streamprocessor-supervisor").ToSome();
                _logger.LogInformation($"EventSaucing started supervision of replica-scoped StreamProcessors of {string.Join(", ", replicaScopedStreamProcessorsProps.Select(x => x.ActorName))}");
            }


            var clusterScopedStreamProcessors = _streamProcessorInitialisation.GetClusterScopedStreamProcessorsInitialisationParameters().ToList();

            if (clusterScopedStreamProcessors.Any()) {
                // Ensure the StreamProcessor checkpoint table is created in the cluster db
                using (var dbConnection = _dbService.GetCluster()) {
                    await ProjectorHelper.InitialiseProjectorStatusStore(dbConnection);
                }

                // foreach akka node role, create the cluster singletons
                foreach (var g in clusterScopedStreamProcessors.GroupBy(x => x.ClusterRole)) {
                    var actor = _actorSystem.ActorOf(
                        ClusterSingletonManager.Props(
                            singletonProps: CreateSupervisorProps(g.Select(x => x)),
                            terminationMessage: PoisonPill.Instance,
                            settings: ClusterSingletonManagerSettings.Create(_actorSystem).WithRole(g.Key)
                        ), name: $"cluster-scoped-streamprocessor-supervisor-{g.Key}");

                    _logger.LogInformation($"EventSaucing started supervision of cluster-scoped StreamProcessors of {string.Join(", ", g.Select(x => x.Props).Select(x => x.TypeName))}");
                }
            }

            _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} started");
        }

        /// <summary>
        /// Stops replica-scoped <see cref="StreamProcessorSupervisor"/>
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} stop requested");

            if (_replicaStreamProcessorSupervisor.HasValue) {
                _logger.LogInformation($"EventSaucing {nameof(StreamProcessorService)} stopping replica scoped {nameof(StreamProcessorSupervisor)}"); 
                //send stop which stops the actor as soon as it has finished processing the current message //https://petabridge.com/blog/how-to-stop-an-actor-akkadotnet/
                var actorRef = _replicaStreamProcessorSupervisor.Get();
                _actorSystem.Stop(actorRef);  
            }

            // i don't think we should shut the cluster-scoped supervisor down, as we don't actually know that the whole cluster is being stopped at this point
            // it might just be our node, let Akka handle shutting down the cluster singleton
            return Task.CompletedTask;
        }
    }
}