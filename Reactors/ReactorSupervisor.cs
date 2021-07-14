using Akka.Actor;
using Akka.DI.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    /// <summary>
    /// The overall supervisor for the reactor infrastructure.  Only one of these needed per cluster. To host a bucket outside of the main node, start a ReactorBucketSupervisor actor instead.
    /// </summary>
    public class ReactorSupervisor : ReceiveActor {
        public ReactorSupervisor(ILogger<ReactorSupervisor> logger) {
            ReceiveAsync<ReactorBucketSupervisor.LocalMessages.SubscribeToBucket>(OnSubscribeToBucketAsync);
            this.logger = logger;
        }

        IActorRef bucketactor;
        private readonly ILogger<ReactorSupervisor> logger;

        protected override void PreStart() {
            logger.LogInformation("ReactorSupervisor starting up. Starting ReactorBucketSupervisor & RoyalMail");
            bucketactor = Context.ActorOf(Context.System.DI().Props<ReactorBucketSupervisor>(), name: "reactor-bucket");
            Context.ActorOf(Context.System.DI().Props<RoyalMail>(), "royal-mail");
        }

        private Task OnSubscribeToBucketAsync(ReactorBucketSupervisor.LocalMessages.SubscribeToBucket msg) {
            logger.LogInformation($"ReactorSupervisor received SubscribeToBucket for '{msg.Bucket}' bucket");

            //just send it on to the bucket actor
            bucketactor.Forward(msg);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Overriding postRestart to disable the call to preStart() after restarts.  This means children are restarted, and we dont create extra instances
        /// </summary>
        /// <param name="reason"></param>
        protected override void PostRestart(Exception reason) { }
    }
}
