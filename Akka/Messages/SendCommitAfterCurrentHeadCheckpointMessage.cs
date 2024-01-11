using System.Diagnostics;
using Scalesque;

namespace EventSaucing.Akka.Messages {
	/// <summary>
	/// Message sent to ask for a commit notification to be ordered
	/// </summary>
	public class SendCommitAfterCurrentHeadCheckpointMessage {

		public long CurrentHeadCheckpoint { get; }

		[DebuggerStepThrough]
		public SendCommitAfterCurrentHeadCheckpointMessage(long currentHeadCheckpoint) {
			CurrentHeadCheckpoint = currentHeadCheckpoint;
		}
	}
}
