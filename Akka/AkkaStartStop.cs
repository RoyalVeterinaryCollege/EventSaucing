using Akka;
using Akka.Actor;
using Autofac;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Akka.DI.Core;

namespace EventSaucing.Akka {
    /// <summary>
    /// Class which waits for the host process to indicate that it is shutting down, then shuts down Akka gracefully
    /// </summary>
    public class AkkaStartStop : IStartable {
        private readonly ILogger<AkkaStartStop> _logger;
        private readonly ActorSystem _actorSystem;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="applicationLifetime"></param>
        /// <param name="actorSystem"></param>
        /// <param name="dependencyResolver">Required.  If you remove this, then autofac starts this class before the actor system is configured to use DI and actors cant be created in Start.</param>
        public AkkaStartStop(ILogger<AkkaStartStop> logger, Microsoft.Extensions.Hosting.IHostApplicationLifetime applicationLifetime, ActorSystem actorSystem, IDependencyResolver dependencyResolver) {
            this._logger = logger;
            this._actorSystem = actorSystem;
            applicationLifetime.ApplicationStopping.Register(StopAkka);
        }
        /// <summary>
        /// ActorSystem is actually started by AutoFac when it is registered. 
        /// </summary>
        public void Start()
        {
            //no op, currently.  But Actors can be started here, in future
        }
        private void StopAkka() {
            _logger.LogInformation("Akka is stopping");
            Task<Done> shutdownTask = CoordinatedShutdown.Get(_actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
            shutdownTask.Wait();
            _logger.LogInformation("Akka is stopped");
        }

    }
}
