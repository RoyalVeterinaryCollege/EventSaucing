using System.Data;
using System.Data.Common;
using Dapper;
using EventSaucing.NEventStore;
using EventSaucing.Storage;
using EventSaucing.StreamProcessors.Projectors;
using ExampleApp.Events;
using NEventStore;
using NEventStore.Persistence;

namespace ExampleApp.OrderCounting
{
    /// <summary>
    /// Alternates between projection + exception to test error handling
    /// </summary>
    public class ErrorThrowingStreamProcessor : SqlProjector
    {
        private readonly IDbService _dbService;

        /// <summary>
        /// Holds if we should throw an error when an exception occurs in a projection method.
        /// </summary>
        static bool _throwError = true;
        static int _count = 0;

        public ErrorThrowingStreamProcessor(IDbService dbService, IPersistStreams persistStreams, Serilog.ILogger logger) : base(persistStreams, logger
            , checkpointPersister: new DeclarativeCheckpointPersister(dbService)
                .TryInitialiseFrom<PersistedSqlProjectorCheckpoint>()
                .TryInitialiseFrom<FirstCommit>()) {
            _dbService = dbService;
        }

        //uses a replica db
        public override DbConnection GetProjectionDb() => _dbService.GetReplica();

        // projection method must start with 'On', have 3 parameters(1st = IDbTransaction, 2nd ICommit, 3rd type of event projected) and return Task.

        public async Task OnOrderPlacedForItem(IDbTransaction tx, ICommit commit, OrderPlacedForItem @evt) {
            _count++;
            if (_throwError) {
                _throwError = false;
                throw new Exception($"ErrorThrowingStreamProcessor: Error thrown as requested {_count}");
            } else {
                // throw next time
                _throwError = true;
            }
        }
    }
}
