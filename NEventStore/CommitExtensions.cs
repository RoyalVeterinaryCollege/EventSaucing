using System;
using NEventStore;
using Scalesque;

namespace EventSaucing.NEventStore {
    public static class CommitExtensions {
        /// <summary>
        ///     Gets the checkpoint token as a long
        /// </summary>
        /// <param name="commit"></param>
        /// <returns></returns>
        public static long CheckpointTokenLong(this ICommit commit) => commit.CheckpointToken.ToLong().Get();

        /// <summary>
        ///     Gets the aggregateId as a guid
        /// </summary>
        /// <param name="commit"></param>
        /// <returns></returns>
        public static Guid AggregateId(this ICommit commit) => commit.StreamId.ToGuid().Get();
    }
}