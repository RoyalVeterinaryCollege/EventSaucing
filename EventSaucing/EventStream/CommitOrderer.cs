﻿using EventSaucing.NEventStore;
using Scalesque;

namespace EventSaucing.EventStream {
    /// <summary>
    /// A simple convenience class for ordering commit messages
    /// </summary>
    public class CommitOrderer {
        readonly CheckpointOrder _order = new CheckpointOrder();

        /// <summary>
        /// Pass your current checkpoint and a new orderedcommitnotification message to determine if this is the next commit for you to process
        /// </summary>
        /// <param name="currentCheckpoint"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public bool CommitFollowsCheckpoint(Option<long> currentCheckpoint, OrderedCommitNotification msg) {
            return _order.Compare(currentCheckpoint, msg.PreviousCheckpoint) == 0;
        }
    }
}
