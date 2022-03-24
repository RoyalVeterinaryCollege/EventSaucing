using System;
using Akka.Configuration;

namespace EventSaucing {
    public class EventSaucingConfiguration {
		/// <summary>
		/// Gets or sets the connection string for the commit store
		/// </summary>
		public string CommitStoreConnectionString { get; set; }
		/// <summary>
		/// Gets or sets teh connection string for the readmodel db
		/// </summary>
        public string ReadmodelConnectionString { get; set; }

		/// <summary>
		/// Gets or sets the maximum number of commits to cache in memory for the projector pipeline. The default is 10.
		/// </summary>
		public int MaxCommitsToCacheInMemory { get; set; } = 10;

		/// <summary>
		/// The name of the akka actorsystem. Defaults to 'EventSaucing'.  All nodes in the akka cluster must use the same name.
		/// </summary>
		public string ActorSystemName { get; set; } = "EventSaucing";

		/// <summary>
		/// The config for akka as read in by <see cref="ConfigurationFactory"/>
		/// </summary>
		public Config AkkaConfig { get; set; }
    }
}