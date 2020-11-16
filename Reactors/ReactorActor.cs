﻿using Akka.Actor;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    /// <summary>
    /// The actor that interacts directly with a reactor to allow it to process its messages
    /// </summary>
    public class ReactorActor : ReceiveActor {
        private readonly IReactorRepository reactorRepo;
        private readonly ILogger<ReactorActor> logger;

        public ReactorActor(IReactorRepository reactorRepo, ILogger<ReactorActor> logger) {
            this.reactorRepo = reactorRepo;
            this.logger = logger;
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

            if (alreadyReacted) {
                logger.LogInformation($"SubscribedAggregateChanged for aggregateid {msg.AggregateId} was sent to reactorId {msg.ReactorId} but the reactor had already processed the stream to (or after) that StreamRevision.");
                logger.LogDebug("{@AggregateSubscriptions}", uow.PersistedPubSub.Get().AggregateSubscriptions);
                return;
            }

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
                // have we already delivered this publication (and if so, have we already seen this version or later)?
                .Map(previous => previous.PublicationDeliveries.Any(sub => sub.PublicationId == msg.PublicationId && sub.VersionNumber >= msg.VersionNumber))
                .GetOrElse(false);

            if (alreadyReacted) {
                logger.LogInformation($"ReactorID {msg.SubscribingReactorId} received article {msg.Name} on subscription id {msg.SubscriptionId} from publishing reactor id {msg.PublishingReactorId} to but the article version {msg.VersionNumber} had already been delivered");
                logger.LogDebug("{@PublicationDeliveries}", uow.PersistedPubSub.Get().PublicationDeliveries);
            }

            //react to msg
            await uow.Reactor.ReactAsync(msg, uow);
            uow.RecordDelivery(msg);

            //persist
            await uow.CompleteAndPublishAsync();
        }
    }
}
