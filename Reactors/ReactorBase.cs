using EventSaucing.Reactors.Messages;
using NEventStore;
using Scalesque;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    /// <summary>
    /// A convenience base class for Reactors; this implementation should be useful for most use-cases.  You don't need to implement ReactorBase, instead you can just implement IReactor.
    /// </summary>
    public abstract class ReactorBase : IReactor {
        protected readonly ConventionalReactionDispatcher dispatcher;
        protected readonly IStoreEvents storeEvents;

        public abstract string Bucket { get; }
        public virtual Option<long> Id { get; set; } = Option.None();
        public virtual int VersionNumber { get; set; }
        public abstract object State { get; set; }

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="dispatcher">ConventionalReactorAggregateEventDispatcher Instatiate this in a static constructor for your Reactor type and call BuildDispatchTable() before instantiating a non-static instance</param>
        public ReactorBase(ConventionalReactionDispatcher dispatcher, IStoreEvents storeEvents) {
            this.dispatcher = dispatcher;
            this.storeEvents = storeEvents;
        }

        public virtual async Task<int> ReactAsync(SubscribedAggregateChanged msg, IUnitOfWork uow) {
            //load any events that we haven't dispatched yet
            var stream = storeEvents.LoadUndispatchedEvents(uow, msg);
            foreach (EventMessage eventMessage in stream.CommittedEvents) {
                await dispatcher.DispatchPayloadAsync(this, eventMessage.Body);
            }
            return stream.StreamRevision;
        }

        public virtual async Task ReactAsync(ArticlePublished msg, IUnitOfWork uow) {
            await dispatcher.DispatchPayloadAsync(this,msg.Article);
        }
    }
}
