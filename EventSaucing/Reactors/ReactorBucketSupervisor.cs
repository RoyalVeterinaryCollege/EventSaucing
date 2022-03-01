using Akka.Actor;
using Akka.Routing;
using System;
using System.Threading.Tasks;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.DependencyInjection;
using EventSaucing.Reactors.Messages;
using Microsoft.Extensions.Configuration;

namespace EventSaucing.Reactors {

    //todo delete ReactorBucketSupervisor?

    /// <summary>
    /// The supervisor for a Reactor bucket.  It subscribes to messages posted to the pub/sub mediator for a particular bucket.
    /// It then forwards these messages to its children, which are a pool of reactor actors.
    /// </summary>
    public class ReactorBucketSupervisor : ReceiveActor {
        private readonly IConfiguration _config;

        /// <summary>
        /// This is 'our' bucket.  We process reactors that are contained in this bucket.
        /// </summary>
        readonly string _bucket;
        /// <summary>
        /// The name of the router for reactor actors (child actors)
        /// </summary>
        const string ReactorActorsRelativeAddress = "reactor-actors";

       
        /// <summary>
        /// 
        /// </summary>
        /// <param name="entityId"></param>
        /// <param name="config"></param>
        public ReactorBucketSupervisor(string entityId, IConfiguration config) {
            _config = config;
            _bucket = config.GetLocalBucketName();

            ReceiveAsync<ArticlePublished>(OnArticlePublishedAsync);
            ReceiveAsync<SubscribedAggregateChanged>(OnSubscribedAggregateChangedAsync);
        }

        protected override void PreStart()
        {
            int instances = _config.GetValue<int?>("EventSaucing:NumberOfReactorActors") ?? 5;

            //These child actors will process any messages on our behalf
            var props = DependencyResolver.For(Context.System)
                .Props<ReactorActor>()
                .WithRouter(new ConsistentHashingPool(instances));
            Context.ActorOf(props, ReactorActorsRelativeAddress);

            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            string topic = GetInternalPublicationTopic(_bucket);
            mediator.Tell(new Subscribe(topic, Self));
        }

        /// <summary>
        /// Overriding postRestart to disable the call to preStart() after restarts.  This means children are restarted, and we don't create extra instances each time
        /// </summary>
        /// <param name="reason"></param>
        protected override void PostRestart(Exception reason)
        {
            // https://getakka.net/articles/actors/receive-actor-api.html#initialization-patterns
            // override and don't call PreStart so PreStart is called only once per instance
        }

        /// <summary>
        /// Gets the internal Akka PubSub topic for a Reactor bucket.  Akka cluster PubSub is used to route reactor messages to the correct bucket.
        /// </summary>
        /// <param name="bucket"></param>
        /// <returns></returns>
        public static string GetInternalPublicationTopic(string bucket) => $"/eventsaucing/reactors/bucket/{bucket}/";

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

     
    }
}
