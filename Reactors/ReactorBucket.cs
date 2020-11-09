using Akka.Actor;
using Akka.DI.Core;
using Akka.Routing;
using System;
using System.Threading.Tasks;
using Akka.Cluster.Tools.PublishSubscribe;

namespace EventSaucing.Reactors {

    /// <summary>
    /// The supervisor for a bucket of reactors.  By default, it will simply restart any actor that throws an exception during message processing.
    /// </summary>
    public class ReactorBucket : ReceiveActor {
        public class LocalMessages {
            /// <summary>
            /// Tells the reactor bucket actor to subscribe to reactor messages for a particular bucket
            /// </summary>
            public class SubscribeToBucket {
                public SubscribeToBucket(string bucket) {
                    if (string.IsNullOrWhiteSpace(bucket)) {
                        throw new ArgumentException($"'{nameof(bucket)}' cannot be null or whitespace", nameof(bucket));
                    }

                    this.Bucket = bucket;
                }

                public string Bucket { get; }
            }
        }

        string bucket;
        public ReactorBucket() {
            ReceiveAsync<LocalMessages.SubscribeToBucket>(OnSubscribeToBucketAsync);
        }

        protected override void PreStart() {
            //todo number of reactor actors from config
            Context.ActorOf(Context.System.DI().Props<ReactorActor>().WithRouter(new ConsistentHashingPool(5)), "reactor-actors");
        }

        /// <summary>
        /// Tells the actor which bucket it is.  Actor subscribes to messags published for that bucket.
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private Task OnSubscribeToBucketAsync(LocalMessages.SubscribeToBucket msg) {
            bucket = msg.Bucket;
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Subscribe($"/reactors/bucket/{bucket}/", Self));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Overriding postRestart to disable the call to preStart() after restarts.  This means children are restarted, and we dont create extra instances each o
        /// </summary>
        /// <param name="reason"></param>
        protected override void PostRestart(Exception reason) {
        }
    }
}
