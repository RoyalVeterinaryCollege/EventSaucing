using System;
using System.Collections.Generic;
using System.Text;
using Akka.Cluster.Sharding;
using EventSaucing.Reactors.Messages;

namespace EventSaucing.Reactors {
    public sealed class ReactorMessageExtractor : HashCodeMessageExtractor {
        readonly int _maxNumberOfShards;
        public ReactorMessageExtractor(int maxNumberOfShards) : base(maxNumberOfShards) {
            _maxNumberOfShards = maxNumberOfShards;
        }

        public override string EntityId(object message) {
            string ReactorIdToEntityId(long reactorId) => (reactorId % _maxNumberOfShards).ToString();
            return message switch {
                ShardRegion.StartEntity start => start.EntityId,
                ArticlePublished msg => ReactorIdToEntityId(msg.SubscribingReactorId),
                SubscribedAggregateChanged msg => ReactorIdToEntityId(msg.ReactorId),
                _ => null
            };
        }

        public override object EntityMessage(object message) => message;
    }
}