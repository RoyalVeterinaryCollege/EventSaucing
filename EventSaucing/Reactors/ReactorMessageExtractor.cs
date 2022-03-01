using System;
using System.Collections.Generic;
using System.Text;
using Akka.Cluster.Sharding;

namespace EventSaucing.Reactors {
    public sealed class ReactorMessageExtractor : HashCodeMessageExtractor {
        public ReactorMessageExtractor(int maxNumberOfShards) : base(maxNumberOfShards) { }

        public override string EntityId(object message)
            => message switch {
                ShardRegion.StartEntity start => start.EntityId,
                ShardEnvelope e => e.ReactorId.ToString(),
                _ => null
            };

        public override object EntityMessage(object message)
            => message switch {
                ShardEnvelope e => e.Payload,
                _ => message
            };
    }

    public sealed class ShardEnvelope
    {
        public int ReactorId { get; }
        public object Payload { get; }

        public ShardEnvelope(int reactorId, object payload)
        {
            ReactorId = reactorId;
            Payload = payload;
        }
    }
}