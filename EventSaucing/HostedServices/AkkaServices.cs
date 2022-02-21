using System;
using System.Threading;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSaucing.HostedServices {
    public class AkkaServices : IHostedService {
        private ActorSystem _actorSystem;
        public IActorRef RouterActor { get; private set; }
        private readonly IServiceProvider _sp;
        private readonly ILogger _logger;

        public AkkaServices(IServiceProvider sp, ILogger logger) {
            _sp = sp;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            // from https://getakka.net/articles/actors/dependency-injection.html

            // var hocon = ConfigurationFactory.ParseString(File.ReadAllText("app.conf"));
            var bootstrap = BootstrapSetup.Create();
            var di = DependencyResolverSetup.Create(_sp);
            var actorSystemSetup = bootstrap.And(di);
            _actorSystem = ActorSystem.Create("AspNetDemo", actorSystemSetup);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Akka is stopping");
            Task<Done> shutdownTask = CoordinatedShutdown.Get(_actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
            return shutdownTask.ContinueWith(t => _logger.LogInformation("Akka is stopped"), cancellationToken);

        }
    }
}
