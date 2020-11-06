using Akka.Actor;
using Akka.DI.Core;

namespace EventSaucing.Reactors {
    /// <summary>
    /// Start the eventsaucing reactor infrastructure
    /// </summary>
    public class ReactorStartup : Autofac.IStartable {
        private readonly ActorSystem system;

        /// <summary>
        /// Instantiates ReactorStartup
        /// </summary>
        /// <param name="system"></param>
        /// <param name="dontremoveitsneeded">This dependency is taken so that akka can create our ReactActors using DI.  Workaround for a race condition in IOC startup, this module starts before we can configure akka to use autofac unless we take a dependency on the resolver itself</param>
        public ReactorStartup(ActorSystem system, IDependencyResolver dontremoveitsneeded) {
            this.system = system;
        }
        public void Start() {
            //start the supervisor
            system.ActorOf(system.DI().Props<ReactorSupervisorActor>(), name:"reactor-supervisor");
        }
    }
}
