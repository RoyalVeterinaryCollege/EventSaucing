using System.Diagnostics;
using NEventStore;
using Scalesque;

namespace EventSaucing.Projectors {
	/// <summary>
	/// A message containing a commit and also the previous checkpoint which allows the commit to be ordered correctly
	/// </summary>
	public class OrderedCommitNotification {
		[DebuggerStepThrough]
		public OrderedCommitNotification(ICommit commit, Option<long> previousCheckpoint) {
			Commit = commit;
			PreviousCheckpoint = previousCheckpoint;
		}

		public ICommit Commit { get; }
		/// <summary>
		/// Gets the checkpoint immediately previous to the attached commit
		/// </summary>
		public Option<long> PreviousCheckpoint { get; }
	}
}
