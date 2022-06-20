using System;
using System.Collections.Generic;
using Akka.Pattern;
using NEventStore;
using NEventStore.Persistence;

namespace EventSaucing.EventStream;

public class FakePersistStreams : IPersistStreams {
    private Queue<ICommit> _queue;

    public FakePersistStreams(IEnumerable<ICommit> commits) {
        _queue = new Queue<ICommit>();
        foreach (var commit in commits) {
            _queue.Enqueue(commit);
        }
    }
    public void Dispose() {
    }

    public IEnumerable<ICommit> GetFrom(string bucketId, string streamId, int minRevision, int maxRevision) {
        throw new NotImplementedException();
    }

    public ICommit Commit(CommitAttempt attempt) {
        throw new NotImplementedException();
    }

    public ISnapshot GetSnapshot(string bucketId, string streamId, int maxRevision) {
        throw new NotImplementedException();
    }

    public bool AddSnapshot(ISnapshot snapshot) {
        throw new NotImplementedException();
    }

    public IEnumerable<IStreamHead> GetStreamsToSnapshot(string bucketId, int maxThreshold) {
        throw new NotImplementedException();
    }

    public void Initialize() {
        throw new NotImplementedException();
    }

    public IEnumerable<ICommit> GetFrom(string bucketId, DateTime start) {
        throw new NotImplementedException();
    }

    public IEnumerable<ICommit> GetFromTo(string bucketId, DateTime start, DateTime end) {
        throw new NotImplementedException();
    }

    public IEnumerable<ICommit> GetFrom(long checkpointToken) {
        throw new NotImplementedException();
    }

    public IEnumerable<ICommit> GetFromTo(long from, long to) {
        if (_queue.Count > 0)
            yield return _queue.Dequeue();
        else 
            yield break;
    }

    public IEnumerable<ICommit> GetFrom(string bucketId, long checkpointToken) {
        throw new NotImplementedException();
    }

    public IEnumerable<ICommit> GetFromTo(string bucketId, long from, long to) {
        throw new NotImplementedException();
    }

    public void Purge() {
        throw new NotImplementedException();
    }

    public void Purge(string bucketId) {
        throw new NotImplementedException();
    }

    public void Drop() {
        throw new NotImplementedException();
    }

    public void DeleteStream(string bucketId, string streamId) {
        throw new NotImplementedException();
    }

    public bool IsDisposed { get; set; }
}