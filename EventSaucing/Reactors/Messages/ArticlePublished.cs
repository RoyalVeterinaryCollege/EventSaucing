using System;
using Akka.Routing;
using Newtonsoft.Json;

namespace EventSaucing.Reactors.Messages {
    /// <summary>
    /// Message sent when a reactor should notified that an article it subscribes to has been published.
    /// </summary>
    public class ArticlePublished : IConsistentHashable {
        public ArticlePublished(string reactorBucket, string name, long subscribingReactorId, long publishingReactorId, int versionNumber, long subscriptionId, long publicationId, string articleSerialisationType, string articleSerialisation) {
            if (string.IsNullOrWhiteSpace(reactorBucket)) {
                throw new System.ArgumentException($"'{nameof(reactorBucket)}' cannot be null or whitespace", nameof(reactorBucket));
            }

            if (string.IsNullOrWhiteSpace(name)) {
                throw new System.ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            ReactorBucket = reactorBucket;
            Name = name;
            SubscribingReactorId = subscribingReactorId;
            PublishingReactorId = publishingReactorId;
            VersionNumber = versionNumber;
            SubscriptionId = subscriptionId;
            PublicationId = publicationId;
            ArticleSerialisationType = articleSerialisationType ?? throw new ArgumentNullException(nameof(articleSerialisationType));
            ArticleSerialisation = articleSerialisation ?? throw new ArgumentNullException(nameof(articleSerialisation));
        }
        public string ReactorBucket { get; }
        public string Name { get; }
        public long SubscribingReactorId { get; }
        public long PublishingReactorId { get; }
        public int VersionNumber { get; }
        public long SubscriptionId { get; }
        public long PublicationId { get; }
        public string ArticleSerialisationType { get; }
        public string ArticleSerialisation { get; }

        /// <summary>
        /// Gets the Article as an object.  You must call this from a process that contains the type 
        /// </summary>
        /// <exception cref="Exception">Thrown when the article cant be deserialised</exception>
        public object Article =>
            JsonConvert.DeserializeObject(ArticleSerialisation,
                Type.GetType(ArticleSerialisationType, throwOnError: true));

        object IConsistentHashable.ConsistentHashKey => SubscribingReactorId; //ensures messages are processed by same reactor actor instance to avoid optimistic concurrency issues
    }
}
