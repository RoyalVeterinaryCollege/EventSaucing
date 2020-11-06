using Akka.Actor;
using Akka.DI.Core;
using Akka.Routing;
using System;

namespace EventSaucing.Reactors {

    /// <summary>
    /// The supervisor for the reactor system.  By default, it will simply restart any actor that throws an exception during message processing.
    /// </summary>
    public class ReactorSupervisorActor : ReceiveActor {

        //initialisation
        //https://getakka.net/articles/actors/receive-actor-api.html#initialization-patterns

        protected override void PreStart() {
            // Initialize children here

            //todo addresses from config?
            //todo number of reactor actors from config
            Context.ActorOf(Context.System.DI().Props<ReactorActor>().WithRouter(new ConsistentHashingPool(5)), "reactor-actors");
            Context.ActorOf(Context.System.DI().Props<RoyalMail>(), "royal-mail");
        }

        /// <summary>
        /// Overriding postRestart to disable the call to preStart() after restarts.  This means children are restarted, and we dont create extra instances each o
        /// </summary>
        /// <param name="reason"></param>
        protected override void PostRestart(Exception reason) {
        }
    }
}
