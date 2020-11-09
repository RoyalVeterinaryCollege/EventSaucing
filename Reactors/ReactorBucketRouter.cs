using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using EventSaucing.Reactors.Messages;

namespace EventSaucing.Reactors {
    /// <summary>
    /// A convenience service which lets you easily publish reactor messages
    /// </summary>
    public interface IReactorBucketRouter {
        void Tell(ArticlePublished msg);
        void Tell(SubscribedAggregateChanged msg);
    }

    public class ReactorBucketRouter : IReactorBucketRouter {
        private readonly ActorSystem system;

        public ReactorBucketRouter(ActorSystem system) {
            this.system = system;
        }

        public void Tell(Messages.ArticlePublished msg) {
            TellInternal(msg.ReactorBucket, msg);
        }

        public void Tell(Messages.SubscribedAggregateChanged msg) {
            TellInternal(msg.ReactorBucket, msg);
        }

        private void TellInternal(string bucket, object msg) {
            var mediator = DistributedPubSub.Get(system).Mediator;
            mediator.Tell(new Publish(ReactorBucket.GetInternalPublicationTopic(bucket), msg));
        }
    }
}
