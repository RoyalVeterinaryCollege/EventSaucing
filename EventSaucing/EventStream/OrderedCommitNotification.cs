using System.Diagnostics;
using NEventStore;
using Scalesque;

namespace EventSaucing.EventStream {
	/// <summary>
	/// A message containing a commit and also the previous checkpoint which allows the commit to be ordered correctly
	/// </summary>
	public class OrderedCommitNotification {
		[DebuggerStepThrough]
		public OrderedCommitNotification(ICommit commit, long previousCheckpoint) {
			Commit = commit;
			PreviousCheckpoint = previousCheckpoint;
		}
		/// <summary>
		/// The commit
		/// </summary>
		public ICommit Commit { get; }
		/// <summary>
		/// Gets the checkpoint immediately previous to the attached commit
		/// </summary>
		public long PreviousCheckpoint { get; }

		public override string ToString() => $"OrderedCommitNotification: {Commit.CheckpointToken}/{Commit.StreamId}/{Commit.CommitSequence}";
	}
}
