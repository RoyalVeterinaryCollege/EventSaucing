using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding;
using EventSaucing.Reactors.Messages;

namespace EventSaucing.Reactors {
    /// <summary>
    /// A convenience service which lets you easily publish reactor messages
    /// </summary>
    public interface IReactorBucketFacade {
        Task TellAsync(ArticlePublished msg);
        Task TellAsync(SubscribedAggregateChanged msg);
    }

    public class ReactorBucketFacade : IReactorBucketFacade {
        readonly Dictionary<string, IActorRef> _proxies = new Dictionary<string, IActorRef>();
        readonly ClusterSharding _sharding;

        public ReactorBucketFacade(ActorSystem system) {
            _sharding = ClusterSharding.Get(system);
        }

        public async Task TellAsync(ArticlePublished msg) {
            if (!_proxies.ContainsKey(msg.ReactorBucket)) {
                await StartProxyAsync(msg.ReactorBucket);
            }

            _proxies[msg.ReactorBucket].Tell(msg);
        }

        public async Task TellAsync(SubscribedAggregateChanged msg) {
            if (!_proxies.ContainsKey(msg.ReactorBucket)) {
                await StartProxyAsync(msg.ReactorBucket);
            }

            _proxies[msg.ReactorBucket].Tell(msg);
        }

        private async Task StartProxyAsync(string bucket) {
            var proxy = await _sharding.StartProxyAsync(
                typeName: "reactors",
                role: $"reactors-{bucket}",
                messageExtractor: new ReactorMessageExtractor(30)); //todo get number of shards from config
            _proxies[bucket] = proxy;
        }
    }
}