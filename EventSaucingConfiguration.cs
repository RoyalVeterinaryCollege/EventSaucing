using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using EventSaucing.Akka.Messages;
using NEventStore;
using Scalesque;

namespace EventSaucing {
    public class EventSaucingConfiguration {
	    /// <summary>
		/// Gets or sets the database connection string used to store and retrieve events.
		/// </summary>
		public string ConnectionString { get; set; }
		/// <summary>
		/// Gets or sets the maximum number of commits to cache in memory. The default is 10.
		/// </summary>
		public int MaxCommitsToCacheInMemory { get; set; } = 10;

		/// <summary>
		/// The name of the akka actorsystem
		/// </summary>
		public string ActorSystemName { get; set; } = "EventSaucing";

		/// <summary>
		/// The config in HCON format for Akka's configuration. See https://getakka.net/articles/concepts/configuration.html
		/// </summary>
		public string AkkaConfiguration { get; set; } = "akka { loglevel=INFO,  loggers=[\"Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog\"]}";
	}
}