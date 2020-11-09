using Akka.Actor;
using Akka.DI.Core;
using System;

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
            //start the supervisor & tell it that it is the cris3 bucket
            system.ActorOf(system.DI().Props<ReactorSupervisor>(), name: "reactor-supervisor");
            ActorSelection bucket = system.ActorSelection("reactor-supervisor/reactor-bucket"); 
            bucket.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket("CRIS3"));
        }
    }

    public class ReactorSupervisor : ReceiveActor {

        protected override void PreStart() {
            Context.ActorOf(Context.System.DI().Props<ReactorBucketSupervisor>(), name: "reactor-bucket");
            Context.ActorOf(Context.System.DI().Props<RoyalMail>(), "royal-mail");
        }

        /// <summary>
        /// Overriding postRestart to disable the call to preStart() after restarts.  This means children are restarted, and we dont create extra instances
        /// </summary>
        /// <param name="reason"></param>
        protected override void PostRestart(Exception reason) { }
    }
}
