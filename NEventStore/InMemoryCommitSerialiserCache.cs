using System.Collections.Generic;
using System.Linq;
using EventSaucing.Akka.Messages;
using NEventStore;
using Scalesque;

namespace EventSaucing.NEventStore {
    public interface IInMemoryCommitSerialiserCache {
        /// <summary>
        /// Caches the commit in memory and trims the cache if it is too large
        /// </summary>
        /// <param name="commit"></param>
        void Cache(ICommit commit);

        List<OrderedCommitNotification> GetCommitsAfter(long checkpoint);
    }

    
    public class InMemoryCommitSerialiserCache : IInMemoryCommitSerialiserCache {
        //checkpoint -> commit
        private readonly Dictionary<long, ICommit> _commits = new Dictionary<long, ICommit>();
        private readonly int _maxNumberOfCommitsToCache;

    
        public InMemoryCommitSerialiserCache(EventSaucingConfiguration config):this(config.MaxCommitsToCacheInMemory) {
        }

        public InMemoryCommitSerialiserCache(int maxNumberOfCommitsToCache) {
            _maxNumberOfCommitsToCache = maxNumberOfCommitsToCache;
        }

        /// <summary>
        /// Caches the commit in memory and trims the cache if it is too ;arge
        /// </summary>
        /// <param name="commit"></param>
        public void Cache(ICommit commit) {
            _commits[commit.CheckpointTokenLong()] = commit; 
            TrimCache();
        }

        public List<OrderedCommitNotification> GetCommitsAfter(long checkpoint) {
            return GetNextOrderedCommits(checkpoint);
        }

        /// <summary>
        /// Reduce the size of the cache so it doesn't exceed the max
        /// </summary>
        private void TrimCache() {
            while (_commits.Count > _maxNumberOfCommitsToCache) {
                var minKey = _commits.Select(x => x.Key).Min(); //remove the oldest commit
                _commits.Remove(minKey);
            }
        }

        /// <summary>
        /// Gets the next commit after a checkpoint from the cache 
        /// </summary>
        /// <param name="checkpoint"></param>
        /// <returns></returns>
        private Option<OrderedCommitNotification> RetrieveNextFromCache(long checkpoint) {
            return _commits
                    .Get(checkpoint + 1) //get next checkpoint's commit
                    .Map(commit => new OrderedCommitNotification(commit, checkpoint.ToSome())); //and create the order
        }

        /// <summary>
        /// Gets all the commits after the checkpoint, this starts the recursion which does the work
        /// </summary>
        /// <param name="checkpoint"></param>
        /// <returns></returns>
        private List<OrderedCommitNotification> GetNextOrderedCommits(long checkpoint) {
            return GetNextOrderedCommitsRecursively(checkpoint, new List<OrderedCommitNotification>());
        }

        /// <summary>
        /// Gets all the commits after the checkpoint recursively
        /// </summary>
        /// <param name="checkpoint"></param>
        /// <param name="results"></param>
        /// <returns></returns>
        private List<OrderedCommitNotification> GetNextOrderedCommitsRecursively(long checkpoint, List<OrderedCommitNotification> results) {
            Option<OrderedCommitNotification> mbNextCommit = RetrieveNextFromCache(checkpoint);
            if (!mbNextCommit.HasValue) return results; //done, no more exist
            
            //else get the commit and any others that exist
            var orderedCommitNotification = mbNextCommit.Get();
            results.Add(orderedCommitNotification);
            return GetNextOrderedCommitsRecursively(orderedCommitNotification.Commit.CheckpointTokenLong(), results);
        }
    }
}