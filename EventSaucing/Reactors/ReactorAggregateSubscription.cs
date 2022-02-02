using System;

namespace EventSaucing.Reactors {

    /// <summary>
    /// Denotes a subscription to an aggregate
    /// </summary>
    public class ReactorAggregateSubscription : IEquatable<ReactorAggregateSubscription> {
        public Guid AggregateId { get; set; }
        /// <summary>
        /// The last stream revision received by the reactor
        /// </summary>
        public int StreamRevision { get; set; }

        public override bool Equals(object obj) {
            var other = obj as ReactorAggregateSubscription;
            if (other != null) return Equals(other);
            return false;
        }

        public override int GetHashCode() {
            return AggregateId.GetHashCode();
        }
        public bool Equals(ReactorAggregateSubscription other) {
            return other.AggregateId == AggregateId;
        }
    }
}
