using Scalesque;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors
{
    public interface IReactor {
        Option<long> Id { get; set; }
        int VersionNumber { get; set; }
        object State { get; set; }
        Task ReactAsync(ReactorActor.LocalMessages.SubscribedAggregateChanged msg, IUnitOfWork uow);
        Task ReactAsync(ReactorActor.LocalMessages.ArticlePublished msg, IUnitOfWork uow);
    }

    /// <summary>
    /// Previously persisted publication and subscription data for the reactor
    /// </summary>
    public class PreviouslyPersistedPubSubData {
        public PreviouslyPersistedPubSubData(IEnumerable<AggregateSubscription> enumerable1, IEnumerable<ReactorSubscription> enumerable2, IEnumerable<ReactorPublication> enumerable3) {
            AggregateSubscriptions = enumerable1.ToList();
            ReactorSubscriptions = enumerable2.ToList();
            Publications = enumerable3.ToList();
        }

        public List<AggregateSubscription> AggregateSubscriptions { get; set; }
        public List<ReactorSubscription> ReactorSubscriptions { get; set; }
        public List<ReactorPublication> Publications { get; set; }
    }
}