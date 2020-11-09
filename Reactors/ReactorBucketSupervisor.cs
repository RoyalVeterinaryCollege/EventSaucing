using Akka.Actor;
using Akka.DI.Core;
using Akka.Routing;
using System;
using System.Threading.Tasks;
using Akka.Cluster.Tools.PublishSubscribe;
using EventSaucing.Reactors.Messages;

namespace EventSaucing.Reactors {

    /// <summary>
    /// The supervisor for a Reactor bucket.  This is the actor which is responsible for handling messages about about reactor articles & subscriptions.
    /// </summary>
    public class ReactorBucketSupervisor : ReceiveActor {
        /// <summary>
        /// Gets the internal Akka PubSub topic for a Reactor bucket.  Akka cluster PubSub is used to route reactor messages to the correct bucket.
        /// </summary>
        /// <param name="bucket"></param>
        /// <returns></returns>
        public static string GetInternalPublicationTopic(string bucket) => $"/reactors/bucket/{bucket}/";
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
        /// <summary>
        /// This is 'our' bucket.  We process reactors that are contained in this bucket.
        /// </summary>
        string bucket;
        /// <summary>
        /// The name of the router for reactor actors (child actors)
        /// </summary>
        const string ReactorActorsRelativeAddress = "reactor-actors";

        public ReactorBucketSupervisor() {
            ReceiveAsync<LocalMessages.SubscribeToBucket>(OnSubscribeToBucketAsync);
            ReceiveAsync<ArticlePublished>(OnArticlePublishedAsync);
            ReceiveAsync<SubscribedAggregateChanged>(OnSubscribedAggregateChangedAsync);
        }

        /// <summary>
        /// Route the message to a child ReactorActor who will actually update the reactor
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private Task OnSubscribedAggregateChangedAsync(SubscribedAggregateChanged msg) {
            Context.ActorSelection(ReactorActorsRelativeAddress).Tell(msg);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Route the message to a child ReactorActor who will actually update the reactor 
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private Task OnArticlePublishedAsync(ArticlePublished msg) {
            Context.ActorSelection(ReactorActorsRelativeAddress).Tell(msg);
            return Task.CompletedTask;
        }

        protected override void PreStart() {
            //todo number of reactor actors from config
            //These child actors will process any messages on our behalf
            Context.ActorOf(Context.System.DI().Props<ReactorActor>().WithRouter(new ConsistentHashingPool(5)), ReactorActorsRelativeAddress);
        }

        /// <summary>
        /// Tells the actor which Reactor bucket it is.  Actor subscribes to messags published for that bucket.
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private Task OnSubscribeToBucketAsync(LocalMessages.SubscribeToBucket msg) {
            bucket = msg.Bucket;
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Subscribe(GetInternalPublicationTopic(bucket), Self));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Overriding postRestart to disable the call to preStart() after restarts.  This means children are restarted, and we dont create extra instances each time
        /// </summary>
        /// <param name="reason"></param>
        protected override void PostRestart(Exception reason) {
        }
    }
}
