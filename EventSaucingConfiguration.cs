namespace EventSaucing {
    public class EventSaucingConfiguration {
		/// <summary>
		/// Gets or sets if you want to use the EventSaucing projector pipeline
		/// </summary>
		public bool UseProjectorPipeline { get; set; }
        /// <summary>
        /// Gets or sets the database connection string which holds the commit store.
        /// </summary>
        public string ConnectionString { get; set; }
		/// <summary>
		/// Gets or sets the maximum number of commits to cache in memory for the projector pipeline. The default is 10.
		/// </summary>
		public int MaxCommitsToCacheInMemory { get; set; } = 10;
		/// <summary>
		/// The name of the akka actorsystem. Defaults to 'EventSaucing'.  All nodes in the akka cluster must use the same name.
		/// </summary>
		public string ActorSystemName { get; set; } = "EventSaucing";
		/// <summary>
		/// The config in HCON format for Akka's configuration. See https://getakka.net/articles/concepts/configuration.html
		/// </summary>
		public string AkkaConfiguration { get; set; } = "akka { loglevel=INFO,  loggers=[\"Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog\"]}";
	}
}