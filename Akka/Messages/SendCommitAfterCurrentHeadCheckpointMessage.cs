using System.Diagnostics;
using Scalesque;

namespace EventSaucing.Akka.Messages {
	/// <summary>
	/// Message sent to ask for a commit notification to be ordered
	/// </summary>
	public class SendCommitAfterCurrentHeadCheckpointMessage {

		public Option<long> CurrentHeadCheckpoint { get; }
		public Option<int> NumberOfCommitsToSend { get; }

		[DebuggerStepThrough]
		public SendCommitAfterCurrentHeadCheckpointMessage(Option<long> currentHeadCheckpoint, Option<int> numberOfCommitsToSend) {
			CurrentHeadCheckpoint = currentHeadCheckpoint;
			NumberOfCommitsToSend = numberOfCommitsToSend;
		}
	}
}
