using System.Threading;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices {
    /// <summary>
    /// A hosted service that shuts Akka down gracefully
    /// </summary>
    public class AkkaShutDownService : IHostedService {
        private readonly ActorSystem _actorSystem;
        private readonly ILogger<EventStreamService> _logger;

        public AkkaShutDownService(ActorSystem actorSystem, ILogger<EventStreamService> logger) {
            _actorSystem = actorSystem;
            _logger = logger;
        }
        public Task StartAsync(CancellationToken cancellationToken) {
            // no op, Akka must be started as a singleton via StartupExtensions.AddEventSaucing()
            // I know this means this class is responsible for shutting down something it didn't create,
            // but I coulsn't find a way of using IHostedServices to start Akka because they all start at once
            // and some other hosted services depend on ActorSystem to start up..
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation($"EventSaucing {nameof(AkkaShutDownService)} stop requested.");
            _logger.LogInformation("Akka is entering co-ordinated shutdown");
            Task<Done> shutdownTask = CoordinatedShutdown.Get(_actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
            return shutdownTask.ContinueWith(t => _logger.LogInformation("Akka is stopped"), cancellationToken);
        }
    }
}