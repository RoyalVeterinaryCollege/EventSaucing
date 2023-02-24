using System.Diagnostics;
using NEventStore;

namespace EventSaucing.EventStream {
	/// <summary>
	///     An actor message wrapping an NEventStore commit (this is sent from the eventstore)
	/// </summary>
	public class CommitNotification {
		[DebuggerStepThrough]
		public CommitNotification(ICommit commit) {
			Commit = commit;
		}

		public ICommit Commit { get; }
	}
}
