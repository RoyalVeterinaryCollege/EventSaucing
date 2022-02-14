using System;
using System.Collections.Generic;
using NEventStore;
using NEventStore.Persistence;

namespace EventSaucing.Projectors {
    public class FakePersistStreams : IPersistStreams {
        public void Dispose() {
            throw new NotImplementedException();
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

        public IEnumerable<ICommit> GetFromTo(long @from, long to) {
            throw new NotImplementedException();
        }

        public IEnumerable<ICommit> GetFrom(string bucketId, long checkpointToken) {
            throw new NotImplementedException();
        }

        public IEnumerable<ICommit> GetFromTo(string bucketId, long @from, long to) {
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
}