using Akka.Actor;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    public class ReactorActor : ReceiveActor {
        private readonly IReactorRepository reactorRepo;
        private readonly IReactorBucketRouter reactorBucketRouter;

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
