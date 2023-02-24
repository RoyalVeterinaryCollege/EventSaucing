using System;
using System.Collections.Generic;
using NEventStore;
using NEventStore.Persistence;

namespace EventSaucing.StreamProcessors {

 
    public class FakePersistStreams : IPersistStreams {
        public void Dispose() {
            throw new Exception();
        }

        public IEnumerable<ICommit> GetFrom(string bucketId, string streamId, int minRevision, int maxRevision) {
            throw new Exception();
        }

        public ICommit Commit(CommitAttempt attempt) {
            throw new Exception();
        }

        public ISnapshot GetSnapshot(string bucketId, string streamId, int maxRevision) {
            throw new Exception();
        }

        public bool AddSnapshot(ISnapshot snapshot) {
            throw new Exception();
        }

        public IEnumerable<IStreamHead> GetStreamsToSnapshot(string bucketId, int maxThreshold) {
            throw new Exception();
        }

        public void Initialize() {
            throw new Exception();
        }

        public IEnumerable<ICommit> GetFrom(string bucketId, DateTime start) {
            throw new Exception();
        }

        public IEnumerable<ICommit> GetFromTo(string bucketId, DateTime start, DateTime end) {
            throw new Exception();
        }

        public IEnumerable<ICommit> GetFrom(long checkpointToken) {
            throw new Exception();
        }

        public IEnumerable<ICommit> GetFromTo(long @from, long to) {
            throw new Exception();
        }

        public IEnumerable<ICommit> GetFrom(string bucketId, long checkpointToken) {
            throw new Exception();
        }

        public IEnumerable<ICommit> GetFromTo(string bucketId, long @from, long to) {
            throw new Exception();
        }

        public void Purge() {
            throw new Exception();
        }

        public void Purge(string bucketId) {
            throw new Exception();
        }

        public void Drop() {
            throw new Exception();
        }

        public void DeleteStream(string bucketId, string streamId) {
            throw new Exception();
        }

        public bool IsDisposed { get; set; }
    }
}