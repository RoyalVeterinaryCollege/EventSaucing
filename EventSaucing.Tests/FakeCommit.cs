using System;
using System.Collections.Generic;
using NEventStore;

namespace EventSaucing {
    public class FakeCommit : ICommit {
        public string BucketId { get; set; }
        public string StreamId { get; set; }
        public int StreamRevision { get; set; }
        public Guid CommitId { get;} = Guid.NewGuid();
        public int CommitSequence { get; set; }
        public DateTime CommitStamp { get; set; }
        public IDictionary<string, object> Headers { get; set; }
        public ICollection<EventMessage> Events { get; set; }
        public long CheckpointToken { get; set; }
    }
}