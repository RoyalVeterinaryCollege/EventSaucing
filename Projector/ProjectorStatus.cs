namespace EventSaucing.Projector {
    /// <summary>
    /// Represents the state a projector has reached with processing events from the distributed eventstores
    /// </summary>
    public class ProjectorStatus {
        public int ProjectorId { get; set; }
        public string ProjectorName { get; set; }
        public long? LastCheckpointToken { get; set; }
    }
}