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
    public class OrderCountingStreamProcessor : SqlProjector {
        private readonly IDbService _dbService;

        public OrderCountingStreamProcessor(IDbService dbService, IPersistStreams persistStreams, Serilog.ILogger logger) : base(persistStreams, logger
            , checkpointPersister: new DeclarativeCheckpointPersister(dbService)
                .TryInitialiseFrom<PersistedSqlProjectorCheckpoint>()
                .TryInitialiseFrom<FirstCommit>()) {
            _dbService = dbService;
        }

        //uses a replica db
        public override DbConnection GetProjectionDb() => _dbService.GetReplica();

        // projection method must start with 'On', have 3 parameters(1st = IDbTransaction, 2nd ICommit, 3rd type of event projected) and return Task.

        public async Task OnOrderPlacedForItem(IDbTransaction tx, ICommit commit, OrderPlacedForItem @evt) {
            var args = new { ItemName=@evt.name, @evt.quantity, OrderId = commit.AggregateId() };

            await tx.Connection.ExecuteAsync(@"
INSERT INTO [dbo].[OrderCounts]
    ([OrderId]
    ,[ItemName]
    ,[Quantity])
SELECT
    @OrderId
    ,@ItemName
    ,0
WHERE NOT EXISTS(SELECT 1 FROM dbo.OrderCounts WHERE OrderId = @OrderId AND @ItemName = @ItemName)
UPDATE dbo.OrderCounts
SET Quantity = Quantity + @Quantity
WHERE OrderId = @OrderId AND @ItemName = @ItemName
", args, tx);
        }
    }
}
