namespace EventSaucing.StreamProcessors
{

    /// <summary>
    /// A cache of status messages for a stream processor
    /// </summary>
    /// <param name="NumberOfStatusToKeep"></param>
    public class StatusMessageCache(int NumberOfStatusToKeep)
    {
        /// <summary>
        /// The last status index we inserted into the statuses array
        /// </summary>
        int _lastStatusIndex = -1;
        public StreamProcessor.Messages.InternalState[] Statuses { get; } = new StreamProcessor.Messages.InternalState[NumberOfStatusToKeep];

        /// <summary>
        /// Adds a status message to the cache.  If the cache is full, the oldest message is overwritten
        /// </summary>
        /// <param name="internalState"></param>
        public void AddStatus(StreamProcessor.Messages.InternalState internalState)  {
            _lastStatusIndex = (_lastStatusIndex + 1) % NumberOfStatusToKeep;
            Statuses[_lastStatusIndex] = internalState;
        }
    }
}
