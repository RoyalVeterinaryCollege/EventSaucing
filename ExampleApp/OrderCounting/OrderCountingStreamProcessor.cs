using System.Data.Common;
using EventSaucing.Storage;
using EventSaucing.StreamProcessors;
using EventSaucing.StreamProcessors.Projectors;
using NEventStore;
using NEventStore.Persistence;

namespace ExampleApp.OrderCounting
{
    public class OrderCountingStreamProcessor : SqlProjector {
        private readonly IDbService _dbService;

        public OrderCountingStreamProcessor(IDbService dbService, IPersistStreams persistStreams,
            SqlProjectorCheckPointPersister persister) : base(persistStreams, persister) {
            _dbService = dbService;
        }

        public override DbConnection GetProjectionDb() => _dbService.GetReplica();

    }
}
