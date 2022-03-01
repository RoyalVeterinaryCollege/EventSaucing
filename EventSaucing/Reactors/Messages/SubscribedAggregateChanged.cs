using Akka.Routing;
using System;

namespace EventSaucing.Reactors.Messages {

    /// <summary>
    /// Message sent when an reactor should be notified that an aggregate subscription has an unprocessed event
    /// </summary>
    public class SubscribedAggregateChanged {
        public SubscribedAggregateChanged(string reactorBucket, long reactorId, Guid aggregateId, int streamRevision) {
            ReactorBucket = reactorBucket;
            ReactorId = reactorId;
            AggregateId = aggregateId;
            StreamRevision = streamRevision;
        }
        public string ReactorBucket { get; }
        public long ReactorId { get; }
        public Guid AggregateId { get; }
        public int StreamRevision { get; }
    }
}
