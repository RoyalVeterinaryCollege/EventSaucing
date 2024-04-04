using System.Collections.Generic;
using System.Linq;
using NEventStore.Persistence;
using Scalesque;

namespace EventSaucing.EventStream {

    /// <summary>
    /// Creates a stream of <see cref="OrderedCommitNotification"/> from IPersistStreams.  The NEventStore implementation slows down after a few pages.
    /// </summary>
    public class OrderedEventStreamer {
        readonly IPersistStreams _persistStreams;
        long _previousCheckpoint;
        readonly Queue<OrderedCommitNotification> _queue = new Queue<OrderedCommitNotification>();

        public OrderedEventStreamer(long startingCheckpoint, IPersistStreams persistStreams) {
            _persistStreams = persistStreams;
            _previousCheckpoint = startingCheckpoint;
            FetchNextPage();
        }
        /// <summary>
        /// Raised when a new page of ICommits is fetched from store
        /// </summary>
        public event System.Action OnPageFetch;
        private void FetchNextPage() {
            // raise the event
            OnPageFetch?.Invoke();

            // fill queue with next page. 'from' is excluded from this
            // we can't simply stream the whole commit store here as a performance issue in NEventstore makes it run extremely slowly
            foreach (var commit in _persistStreams.GetFrom(_previousCheckpoint).Take(512)) {
                _queue.Enqueue(new OrderedCommitNotification(commit, _previousCheckpoint));
                _previousCheckpoint = commit.CheckpointToken;
            }

            IsFinished = _queue.Count == 0;
        }
        public bool IsFinished { get; private set; }

        public Option<OrderedCommitNotification> Peek() {
            // if q is empty, try to fetch next page
            if (_queue.Count == 0) {
                FetchNextPage();
            }
            if (_queue.Count == 0) {
                // it's still empty, we're done
                return Option.None();
            } else { 
                return _queue.Peek().ToSome(); 
            }
        }

        public OrderedCommitNotification Next() {
            var nextCommit = _queue.Dequeue();
            if (_queue.Count == 0)   {
                FetchNextPage();
            }
            return nextCommit;
        }
    }
}