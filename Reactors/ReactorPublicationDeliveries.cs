using System;

namespace EventSaucing.Reactors {
    public class ReactorPublicationDeliveries {
        public long SubscriptionId { get; set; }
        public long PublicationId { get; set; }
        public int VersionNumber { get; set; }
        public DateTime LastDeliverDate { get; set; }
    }
}
