using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Routing;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    public class ReactorActor : ReceiveActor {
        private readonly IReactorRepository reactorRepo;
        private readonly IReactorBucketRouter reactorBucketRouter;

        /// <summary>
        /// These should all be hashed by the reactorid.  This means that a given reactor will always be processed on the same actor which means that we shouldn't get optimistic concurency clashes when saving actors.
        /// </summary>
        public class LocalMessages {
            /// <summary>
            /// Message sent when a reactor should notiied that an article it subscribes to has been published.
            /// </summary>
            [Obsolete]
            public class ArticlePublished : IConsistentHashable {
               /* public ArticlePublished(string reactorBucket) {
                    ReactorBucket = reactorBucket;
                }*/
                public string ReactorBucket { get; set; }
                public long SubscribingReactorId { get; set; }
                public long PublishingReactorId { get; set; }
                public int VersionNumber { get; set; }
                public object Article { get; set; }
                public long SubscriptionId { get; set; } 
                public long PublicationId { get;  set; } 

                object IConsistentHashable.ConsistentHashKey => SubscribingReactorId;
            }

            /// <summary>
            /// Message sent when an reactor should be notified that an aggregate subscription has an unprocessed event
            /// </summary>
            [Obsolete]

            public class SubscribedAggregateChanged : IConsistentHashable {
               /*
                public SubscribedAggregateChanged(string reactorBucket) {
                    ReactorBucket = reactorBucket;
                }*/
                public string ReactorBucket { get; set; }
                public long ReactorId { get; set; }
                public Guid AggregateId { get; set; }
                public int StreamRevision { get; set; }
                object IConsistentHashable.ConsistentHashKey => ReactorId;
            }
        }

        public ReactorActor(IReactorRepository reactorRepo, IReactorBucketRouter reactorBucketRouter) {
            this.reactorRepo = reactorRepo;
            this.reactorBucketRouter = reactorBucketRouter;
            ReceiveAsync<Messages.ArticlePublished>(OnArticlePublishedAsync);
            ReceiveAsync<Messages.SubscribedAggregateChanged>(OnSubscribedAggregateChangedAsync);
        }

        private async Task OnSubscribedAggregateChangedAsync(Messages.SubscribedAggregateChanged msg) {
            IUnitOfWork uow = await reactorRepo.LoadAsync(msg.ReactorId);

            // guard race condition where a reactor has already caught up, or unsubscribed
            bool subscriptionOutstanding = uow.Previous
                .Map(previous => previous.AggregateSubscriptions.Any(sub => sub.AggregateId == msg.AggregateId && sub.StreamRevision < msg.StreamRevision))
                .GetOrElse(false);
            if (!subscriptionOutstanding) return;

            await uow.Reactor.ReactAsync(msg, uow);

            //persist and get publication messages
            var articleMsgs = await uow.CompleteAsync();

            //send any publications
            foreach (Messages.ArticlePublished articleMsg in articleMsgs) {
                reactorBucketRouter.Tell(articleMsg);
            }
        }

        /// <summary>
        /// called when an an article is published that the reactor subscribes to
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private async Task OnArticlePublishedAsync(Messages.ArticlePublished msg) {
            IUnitOfWork uow = await reactorRepo.LoadAsync(msg.SubscribingReactorId);

            // todo guard race condition where a reactor has already caught up, or unsubscribed

            await uow.Reactor.ReactAsync(msg, uow);
            uow.RecordDelivery(new ReactorPublicationDelivery { PublicationId = msg.PublicationId, SubscriptionId = msg.SubscriptionId, VersionNumber = msg.VersionNumber });

            //persist and get publication messages
            System.Collections.Generic.IEnumerable<Messages.ArticlePublished> articleMsgs = await uow.CompleteAsync();

            // send any publications to the relevant bucket
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            foreach (Messages.ArticlePublished articleMsg in articleMsgs) {
                reactorBucketRouter.Tell(articleMsg);
            }
        }

        private class ReactorSubscribersToUpdate {
            public long PublicationId { get; set; }
            public long SubscribingReactorId { get; set; }
        }
    }
}
