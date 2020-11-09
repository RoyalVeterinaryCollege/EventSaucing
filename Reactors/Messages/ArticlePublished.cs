using Akka.Routing;

namespace EventSaucing.Reactors.Messages {
    /// <summary>
    /// Message sent when a reactor should notified that an article it subscribes to has been published.
    /// </summary>
    public class ArticlePublished : IConsistentHashable {
        public ArticlePublished(string reactorBucket, long subscribingReactorId, long publishingReactorId, int versionNumber, long subscriptionId, long publicationId) {
             ReactorBucket = reactorBucket;
            SubscribingReactorId = subscribingReactorId;
            PublishingReactorId = publishingReactorId;
            VersionNumber = versionNumber;
            SubscriptionId = subscriptionId;
            PublicationId = publicationId;
        }
        public string ReactorBucket { get; }
        public long SubscribingReactorId { get; }
        public long PublishingReactorId { get; }
        public int VersionNumber { get; }
        public object Article { get; }
        public long SubscriptionId { get; }
        public long PublicationId { get; }

        object IConsistentHashable.ConsistentHashKey => SubscribingReactorId;
    }
}
