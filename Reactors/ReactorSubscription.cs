using Scalesque;

namespace EventSaucing.Reactors {

    /// <summary>
    /// Declares a subscription to a publication
    /// </summary>
    public class ReactorSubscription {

        public Option<long> Id { get; set; } = Option.None();

        private string name;

        /// <summary>
        /// The name of the publication
        /// </summary>
        public string Name {
            get => name;
            set {
                ReactorPublication.GuardPublicationName(value);
                name = value;
            }
        }

        /// <summary>
        /// A hash for the name to speed up db searches
        /// </summary>
        public int NameHash { get => name.GetHashCode(); }
    }
    /// <summary>
    /// Represents a delivery of an article to a subscriber
    /// </summary>
    public class ReactorPublicationDelivery {
        public long SubscriptionId { get; set; }
        public long PublicationId { get; set; }
        public int VersionNumber { get; set; }
    }
}
