using System;

namespace EventSaucing.Reactors {

    /// <summary>
    /// Denotes a subscription to an aggregate
    /// </summary>
    public class AggregateSubscription {
        public Guid AggregateId { get; set; }
        /// <summary>
        /// The last stream revision received by the reactor
        /// </summary>
        public int StreamRevision { get; set; }

    }
}
