using System;
using System.Collections.Generic;
using NEventStore;
using NEventStore.Persistence;

namespace EventSaucing.EventStream {

    /// <summary>
    /// Creates a stream of <see cref="OrderedCommitNotification"/> from an IEnumerable ICommit such as the one returned by IPersistStreams
    /// </summary>
    public class OrderedEventStreamer : IDisposable {
        readonly IPersistStreams _persistStreams;
        IEnumerator<ICommit> _enumerator;
        long _previousCheckpoint;

        public OrderedEventStreamer(long startingCheckpoint, IPersistStreams persistStreams) {
            _persistStreams = persistStreams;
            // from is excluded from this, to is included 
            _enumerator =  persistStreams.GetFromTo(startingCheckpoint, startingCheckpoint + 512).GetEnumerator();
            IsFinished = !_enumerator.MoveNext();
            _previousCheckpoint = startingCheckpoint;
        }

        public bool IsFinished { get; private set; }

        public OrderedCommitNotification Next() {
            var orderedCommitNotification = new OrderedCommitNotification(_enumerator.Current, _previousCheckpoint);

            //store this for next iteration
            _previousCheckpoint = orderedCommitNotification.Commit.CheckpointToken;

            if (!_enumerator.MoveNext()) {
                // if the current enumeration has completed, fetch the next page from the db

                // from is excluded from this, to is included
                _enumerator = _persistStreams.GetFromTo(orderedCommitNotification.Commit.CheckpointToken, orderedCommitNotification.Commit.CheckpointToken + 512).GetEnumerator();
                IsFinished = !_enumerator.MoveNext(); //only finished if the final page fetch is empty
            }
            return orderedCommitNotification;

            /*
            long previous = _previousCheckpoint;
            ICommit commit = _enumerator.Current;
            _previousCheckpoint = commit.CheckpointToken;
            var result = new OrderedCommitNotification(commit, previous);
            IsFinished = !_enumerator.MoveNext();
            return result;
            */
        }

        void IDisposable.Dispose() {
            _enumerator.Dispose();
        }
    }
}