using Akka.Actor;
using Akka.DI.Core;
using System;

namespace EventSaucing.Reactors {
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
