using System;

namespace EventSaucing {
    public class EventSaucingConfiguration {
		/// <summary>
		/// Gets or sets the connection string for the commit store
		/// </summary>
		public string CommitStoreConnectionString { get; set; }
		/// <summary>
		/// Gets or sets teh connection string for the replica
		/// </summary>
        public string ReplicaConnectionString { get; set; }

		/// <summary>
		/// Gets or sets the maximum number of commits to cache in memory for the projector pipeline. The default is 10.
		/// </summary>
		public int MaxCommitsToCacheInMemory { get; set; } = 10;

		/// <summary>
		/// The name of the akka actorsystem. Defaults to 'EventSaucing'.  All nodes in the akka cluster must use the same name.
		/// </summary>
		public string ActorSystemName { get; set; } = "EventSaucing";
	}
}