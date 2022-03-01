using System.Collections.Generic;
using Akka.Actor;
using Akka.Cluster.Sharding;
using EventSaucing.Reactors.Messages;

namespace EventSaucing.Reactors {
    /// <summary>
    /// A convenience service which lets you easily publish reactor messages
    /// </summary>
    public interface IReactorBucketFacade {
        void Tell(ArticlePublished msg);
        void Tell(SubscribedAggregateChanged msg);
    }

    public class ReactorBucketFacade : IReactorBucketFacade {
        readonly Dictionary<string, IActorRef> _proxies = new Dictionary<string, IActorRef>();
        readonly ClusterSharding _sharding;

        public ReactorBucketFacade(ActorSystem system) {
            _sharding = ClusterSharding.Get(system);
        }

        public void Tell(ArticlePublished msg) {
            if (!_proxies.ContainsKey(msg.ReactorBucket)) {
                StartProxy(msg.ReactorBucket);
            }

            _proxies[msg.ReactorBucket].Tell(msg);
        }

        public void Tell(SubscribedAggregateChanged msg) {
            if (!_proxies.ContainsKey(msg.ReactorBucket)) {
                StartProxy(msg.ReactorBucket);
            }

            _proxies[msg.ReactorBucket].Tell(msg);
        }

        private void StartProxy(string bucket) {
            var proxy = _sharding.StartProxy(
                typeName: "reactors",
                role: $"reactors-{bucket}",
                messageExtractor: new ReactorMessageExtractor(30)); //todo get number of shards from config
            _proxies[bucket] = proxy;
        }
    }
}