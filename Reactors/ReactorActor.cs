using Akka.Actor;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    /// <summary>
    /// The actor that interacts directly with a reactor to allow it to process its messages
    /// </summary>
    public class ReactorActor : ReceiveActor {
        private readonly IReactorRepository reactorRepo;

        public ReactorActor(IReactorRepository reactorRepo) {
            this.reactorRepo = reactorRepo;
            ReceiveAsync<Messages.ArticlePublished>(OnArticlePublishedAsync);
            ReceiveAsync<Messages.SubscribedAggregateChanged>(OnSubscribedAggregateChangedAsync);
        }

        private async Task OnSubscribedAggregateChangedAsync(Messages.SubscribedAggregateChanged msg) {
            IUnitOfWorkInternal uow = (IUnitOfWorkInternal)await reactorRepo.LoadAsync(msg.ReactorId);

            // guard race condition where a reactor has already caught up
            bool alreadyReacted = uow.PersistedPubSub
                // have we already delivered this aggregate version?
                .Map(previous => previous.AggregateSubscriptions.Any(sub => sub.AggregateId == msg.AggregateId && sub.StreamRevision >= msg.StreamRevision))
                .GetOrElse(false);
            if (alreadyReacted) return;

            //react to msg
            int newStreamRevision = await uow.Reactor.ReactAsync(msg, uow);
            uow.RecordDelivery(msg.AggregateId, newStreamRevision);

            //persist 
            await uow.CompleteAndPublishAsync();
        }

        private async Task OnArticlePublishedAsync(Messages.ArticlePublished msg) {
            IUnitOfWorkInternal uow = (IUnitOfWorkInternal)await reactorRepo.LoadAsync(msg.SubscribingReactorId);

            // guard race condition where a reactor has already caught up
            bool alreadyReacted = uow.PersistedPubSub
                // have we already delivered this publication version?
                .Map(previous => previous.PublicationDeliveries.Any(sub => sub.SubscriptionId == msg.SubscriptionId && sub.VersionNumber >= msg.VersionNumber))
                .GetOrElse(false);
            if (alreadyReacted) return;

            //react to msg
            await uow.Reactor.ReactAsync(msg, uow);
            uow.RecordDelivery(msg);

            //persist
            await uow.CompleteAndPublishAsync();
        }
    }
}
