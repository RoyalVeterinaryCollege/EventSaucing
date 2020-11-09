using Akka.Routing;

namespace EventSaucing.Reactors.Messages {
    /// <summary>
    /// Message sent when a reactor should notified that an article it subscribes to has been published.
    /// </summary>
    public class ArticlePublished : IConsistentHashable {
        public ArticlePublished(string reactorBucket, long subscribingReactorId, long publishingReactorId, int versionNumber, long subscriptionId, long publicationId, object article) {
            if (string.IsNullOrWhiteSpace(reactorBucket)) {
                throw new System.ArgumentException($"'{nameof(reactorBucket)}' cannot be null or whitespace", nameof(reactorBucket));
            }

            ReactorBucket = reactorBucket;
            SubscribingReactorId = subscribingReactorId;
            PublishingReactorId = publishingReactorId;
            VersionNumber = versionNumber;
            SubscriptionId = subscriptionId;
            PublicationId = publicationId;
            Article = article ?? throw new System.ArgumentNullException(nameof(article));
        }
        public string ReactorBucket { get; }
        public long SubscribingReactorId { get; }
        public long PublishingReactorId { get; }
        public int VersionNumber { get; }
        public long SubscriptionId { get; }
        public long PublicationId { get; }
        public object Article { get; }
        object IConsistentHashable.ConsistentHashKey => SubscribingReactorId; //ensures messages are processed by same reactor actor instance to avoid optimistic concurrency issues
    }
}
