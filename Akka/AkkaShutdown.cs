using Akka;
using Akka.Actor;
using Autofac;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace EventSaucing.Akka {
    /// <summary>
    /// Class which waits for the host process to indicate that it is shutting down, then shuts down Akka gracefully
    /// </summary>
    public class AkkaShutdown : IStartable {
        private readonly ILogger<AkkaShutdown> logger;
        private readonly ActorSystem actorSystem;
        
        public AkkaShutdown(ILogger<AkkaShutdown> logger, Microsoft.Extensions.Hosting.IHostApplicationLifetime applicationLifetime, ActorSystem actorSystem) {
            this.logger = logger;
            this.actorSystem = actorSystem;
            applicationLifetime.ApplicationStopping.Register(StopAkka);
        }

        private void StopAkka() {
            logger.LogInformation("Akka is stopping");
            Task<Done> shutdownTask = CoordinatedShutdown.Get(actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
            shutdownTask.Wait();
            logger.LogInformation("Akka is stopped");
        }

        public void Start() {
            //no op on startup, just wait for ASP.Net to shutdown
        }
    }
}
