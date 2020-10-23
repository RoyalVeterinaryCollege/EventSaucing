using System.Collections.Generic;
using NEventStore;

namespace EventSaucing.NEventStore {
    /// <summary>
    /// Compares commits on the basis of their checkpoint tokens
    /// </summary>
    class CommitComparer : IComparer<ICommit> {
        public int Compare(ICommit x, ICommit y) => x.CheckpointToken.CompareTo(y.CheckpointToken);
    }
}