using Scalesque;
using System;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
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
}