using EventSaucing.Reactors.Messages;
using NEventStore;
using System;
using System.Linq;

namespace EventSaucing.Reactors {
    public static class ReactorExtensionMethods {

        /// <summary>
        /// Loads the events from the eventstream which have not yet been dispatched to the Reactor
        /// </summary>
        /// <param name="storeEvents"></param>
        /// <param name="uow"></param>
        /// <param name="msg"></param>
        /// <returns>IEventStream the (partial) stream of events which have not yet been dispatched to the Reactor</returns>
        public static IEventStream LoadUndispatchedEvents(this IStoreEvents storeEvents, IUnitOfWork uow, SubscribedAggregateChanged msg) {
            int fromStreamRevision = 
                uow.PersistedPubSub
                   .Map(previous => previous.AggregateSubscriptions.First(x => x.AggregateId == msg.AggregateId).StreamRevision + 1)
                   .GetOrElse(1);
            return LoadUndispatchedEvents(storeEvents, msg.AggregateId, fromStreamRevision);
        }
        /// <summary>
        /// Loads the events from the eventstream optionally after a particular StreamRevision
        /// </summary>
        /// <param name="storeEvents"></param>
        /// <returns>IEventStream the (potentially partial) stream of events which have not yet been dispatched to the Reactor</returns>
        public static IEventStream LoadUndispatchedEvents(this IStoreEvents storeEvents, Guid aggregateId, int streamRevision = 1) {
            return storeEvents.OpenStream(aggregateId, streamRevision);
        }
    }
}
