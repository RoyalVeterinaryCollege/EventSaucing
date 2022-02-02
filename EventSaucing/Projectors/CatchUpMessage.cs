namespace EventSaucing.Projectors {

	/// <summary>
	/// A message sent by the CommitSerialisor when it first starts (and is therefore unable to order commits)
	/// </summary>
	public class CatchUpMessage {
	}
}
