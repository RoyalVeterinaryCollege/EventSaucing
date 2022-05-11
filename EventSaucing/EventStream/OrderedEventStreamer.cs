using System.Collections.Generic;
using NEventStore.Persistence;

namespace EventSaucing.EventStream {

    /// <summary>
    /// Creates a stream of <see cref="OrderedCommitNotification"/> from IPersistStreams
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
        private void FetchNextPage()  {
            //fill queue with next page. 'from' is excluded from this, 'to' is included 
            foreach (var commit in _persistStreams.GetFromTo(from: _previousCheckpoint, to: _previousCheckpoint + 512)) {
                _queue.Enqueue(new OrderedCommitNotification(commit, _previousCheckpoint));
                _previousCheckpoint = commit.CheckpointToken;
            }

            IsFinished = _queue.Count == 0;
        }
        public bool IsFinished { get; private set; }

        public OrderedCommitNotification Next() {
            var nextCommit = _queue.Dequeue();
            if (_queue.Count == 0)   {
                FetchNextPage();
            }
            return nextCommit;
        }
    }
}