using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.NEventStore;
using EventSaucing.Projectors;
using EventSaucing.Reactors.Messages;
using EventSaucing.Storage;
using Microsoft.Extensions.Configuration;
using NEventStore;
using NEventStore.Persistence;
using Serilog;

namespace EventSaucing.Reactors {

    //todo: ReactorAggregateSubscriptionProjector needs to initialise at head, also needs to be cluster aware??

    /// <summary>
    /// A projector that sends SubscribedAggregateChanged messages to Reactors who have subscribed to those aggregates
    /// </summary>
    public class ReactorAggregateSubscriptionProjector : SqlProjector  {
        private readonly IReactorBucketFacade _reactorBucketRouter;
        private readonly string _bucket;

        public ReactorAggregateSubscriptionProjector(IPersistStreams persistStreams, ILogger logger, IDbService dbService, IConfiguration config, IReactorBucketFacade reactorBucketRouter) : base(persistStreams, logger, config, dbService) {
            _reactorBucketRouter = reactorBucketRouter;
            _bucket = config.GetLocalBucketName();
        }

        public override DbConnection GetProjectionDb() {
            return _dbService.GetReplica();
        }

        public override async Task<bool> ProjectAsync(ICommit commit) {
            throw new NotImplementedException("dont do anything until the todo above is fixed");

            using (var con = _dbService.GetReplica()) {
                await con.OpenAsync();

                const string sql = @"
SELECT 
	R.Bucket AS [ReactorBucket],
	RAS.ReactorId,
	@AggregateId,
	@StreamRevision [StreamRevision]

FROM
	dbo.ReactorAggregateSubscriptions RAS

	INNER JOIN dbo.Reactors R
		ON RAS.ReactorId = R.Id
WHERE
	RAS.AggregateId = @AggregateId
	AND RAS.StreamRevision < @StreamRevision
    AND R.Bucket = @Bucket";

                //Look for aggregate subscriptions that need to be updated in our bucket
                var aggregateSubscriptionMessages = (await con.QueryAsync<SubscribedAggregateChanged>(sql,
                    new { AggregateId = commit.AggregateId(), commit.StreamRevision, Bucket = _bucket })).ToList();

                foreach (var msg in aggregateSubscriptionMessages) {
                    await _reactorBucketRouter.TellAsync(msg);
                }

                // persist checkpoint if we sent any messages to avoid resending them on restart
                return aggregateSubscriptionMessages.Any();
            }
        }
    }
}
