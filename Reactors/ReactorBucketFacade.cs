using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using EventSaucing.Reactors.Messages;

namespace EventSaucing.Reactors {
    /// <summary>
    /// A convenience service which lets you easily publish reactor messages
    /// </summary>
    public interface IReactorBucketFacade {
        void Tell(ArticlePublished msg);
        void Tell(SubscribedAggregateChanged msg);
    }

    public class ReactorBucketFacade : IReactorBucketFacade {
        private readonly ActorSystem system;

        public ReactorBucketFacade(ActorSystem system) {
            this.system = system;
        }

        public void Tell(ArticlePublished msg) {
            TellInternal(msg.ReactorBucket, msg);
        }

        public void Tell(SubscribedAggregateChanged msg) {
            TellInternal(msg.ReactorBucket, msg);
        }

        private void TellInternal(string bucket, object msg) {
            var mediator = DistributedPubSub.Get(system).Mediator;
            mediator.Tell(new Publish(ReactorBucketSupervisor.GetInternalPublicationTopic(bucket), msg));
        }
    }
}
