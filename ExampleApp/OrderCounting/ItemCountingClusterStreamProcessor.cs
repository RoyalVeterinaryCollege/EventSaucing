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
    public class ItemCountingClusterStreamProcessor : SqlProjector {
        private readonly IDbService _dbService;

        public ItemCountingClusterStreamProcessor(IDbService dbService, IPersistStreams persistStreams,
            SqlProjectorCheckPointPersister persister) : base(persistStreams, persister) {
            _dbService = dbService;
        }

        // uses the cluster db
        public override DbConnection GetProjectionDb() => _dbService.GetCluster();

        // projection method must start with 'On', have 3 parameters(1st = IDbTransaction, 2nd ICommit, 3rd type of event projected) and return Task.

        public async Task OnOrderPlacedForItem(IDbTransaction tx, ICommit commit, OrderPlacedForItem @evt) {
            var args = new { Item=@evt.name, @evt.quantity };

            await tx.Connection.ExecuteAsync(@"
INSERT INTO [dbo].[ItemCounts]
    ([Item]
    ,[Count])
SELECT
    @Item
    ,0
WHERE NOT EXISTS(SELECT 1 FROM dbo.ItemCounts WHERE @Item = Item)
UPDATE dbo.ItemCounts
SET Count = Count + @quantity
WHERE Item = @Item
", args, tx);
        }
    }
}
