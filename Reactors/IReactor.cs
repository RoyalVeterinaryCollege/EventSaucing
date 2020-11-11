using Scalesque;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors
{
    public interface IReactor {
        string Bucket { get; }
        Option<long> Id { get; set; }
        int VersionNumber { get; set; }
        object State { get; set; }
        /// <summary>
        /// Tells reactor to react to the newly changed aggregate. 
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="uow"></param>
        /// <returns>int The last StreamRevision of the aggregate that the Reactor has processed</returns>
        Task<int> ReactAsync(Messages.SubscribedAggregateChanged msg, IUnitOfWork uow);
        Task ReactAsync(Messages.ArticlePublished msg, IUnitOfWork uow);
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