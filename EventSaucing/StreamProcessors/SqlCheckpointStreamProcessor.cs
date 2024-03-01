using EventSaucing.NEventStore;
using NEventStore;
using NEventStore.Persistence;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.StreamProcessors {

	public abstract class SqlCheckpointStreamProcessor : StreamProcessor {
		private readonly ILogger logger;
		protected readonly ConventionBasedEventDispatcher eventDispatcher;

		public SqlCheckpointStreamProcessor(IPersistStreams persistStreams, ILogger logger, IStreamProcessorCheckpointPersister checkpointPersister) : base(persistStreams, checkpointPersister) {
			this.logger = logger.ForContext(GetType());
			this.eventDispatcher = new ConventionBasedEventDispatcher(this);
		}

		public override async Task<bool> ProcessAsync(ICommit commit) {

			var streamProcessorMethods = eventDispatcher.GetStreamProcessorMethods(commit).ToList();

			if (streamProcessorMethods.Any()) {
				foreach (var (streamProcessorMethod, evt) in streamProcessorMethods) {
					await streamProcessorMethod(commit, evt);
				}
			}

			return true;
		}
	}
}
