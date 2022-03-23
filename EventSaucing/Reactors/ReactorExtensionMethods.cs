using EventSaucing.Reactors.Messages;
using NEventStore;
using System;
using System.Linq;
using System.Threading.Tasks;

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
        /// <param name="aggregateId"></param>
        /// <param name="streamRevision"></param>
        /// <returns>IEventStream the (potentially partial) stream of events which have not yet been dispatched to the Reactor</returns>
        public static IEventStream LoadUndispatchedEvents(this IStoreEvents storeEvents, Guid aggregateId, int streamRevision = 1) {
            return storeEvents.OpenStream(aggregateId, streamRevision);
        }

        /// <summary>
        /// Dispatches the eventstream to the reactor
        /// </summary>
        /// <param name="reactor"></param>
        /// <param name="storeEvents"></param>
        /// <param name="dispatcher"></param>
        /// <param name="aggregateId"></param>
        /// <param name="streamRevision"></param>
        /// <returns>int The last streamrevision that was dispatched</returns>
        public static async Task<int> DispatchEventStreamAsync(this IReactor reactor, IStoreEvents storeEvents, ConventionalReactionDispatcher dispatcher, Guid aggregateId, int streamRevision = 1) {
            var stream = storeEvents.OpenStream(aggregateId, streamRevision);
            await dispatcher.DispatchEventStreamAsync(reactor, stream);
            return stream.StreamRevision;
        }
    }
}
