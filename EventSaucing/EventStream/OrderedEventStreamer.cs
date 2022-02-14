using System;
using System.Collections.Generic;
using System.Text;
using NEventStore;
using Scalesque;

namespace EventSaucing.EventStream {

    /// <summary>
    /// Creates a stream of <see cref="OrderedCommitNotification"/> from an IEnumerable<ICommit> such as the one returned by IPersistStreams
    /// </summary>
    public class OrderedEventStreamer : IDisposable {
        private long _previousCheckpoint;
        readonly IEnumerator<ICommit> _enumerator;

        public OrderedEventStreamer(long startingCheckpoint, IEnumerable<ICommit> commitStream) {
            _previousCheckpoint = startingCheckpoint;
            _enumerator = commitStream.GetEnumerator();

            IsFinished = !_enumerator.MoveNext();

        }

        public bool IsFinished { get; private set; }

        public OrderedCommitNotification Next() {
            var previous = _previousCheckpoint;
            var commit = _enumerator.Current;
            _previousCheckpoint = commit.CheckpointToken;
            var result = new OrderedCommitNotification(commit, previous.ToSome());
            IsFinished = !_enumerator.MoveNext();
            return result;
        }

        void IDisposable.Dispose() {
            _enumerator.Dispose();
        }
    }
}