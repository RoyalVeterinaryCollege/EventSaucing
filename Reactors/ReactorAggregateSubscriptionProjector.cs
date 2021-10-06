using System.Linq;
using Dapper;
using EventSaucing.NEventStore;
using EventSaucing.Projectors;
using EventSaucing.Reactors.Messages;
using EventSaucing.Storage;
using Microsoft.Extensions.Configuration;
using NEventStore;
using NEventStore.Persistence;

namespace EventSaucing.Reactors {
    /// <summary>
    /// A projector that sends SubscribedAggregateChanged messages to Reactors who have subscribed to those aggregates
    /// </summary>
    public class ReactorAggregateSubscriptionProjector : ProjectorBase  {
        private readonly IReactorBucketFacade _reactorBucketRouter;
        private readonly string _bucket;

        public ReactorAggregateSubscriptionProjector(IPersistStreams persistStreams, IDbService dbService, IConfiguration config, IReactorBucketFacade reactorBucketRouter) : base(persistStreams, dbService, ) {
            _reactorBucketRouter = reactorBucketRouter;
            _bucket = config.GetLocalBucketName();
        }
        
        public override void Project(ICommit commit) {
            
            using (var con = _dbService.GetConnection()) {
                con.Open();
                
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
                var aggregateSubscriptionMessages = (con.Query<SubscribedAggregateChanged>(sql, new { AggregateId = commit.AggregateId(), commit.StreamRevision, Bucket=_bucket })).ToList();

                foreach (var msg in aggregateSubscriptionMessages) {
                    _reactorBucketRouter.Tell(msg);
                }

                // persist checkpoint if we sent any messages to avoid resending them on restart
                if (aggregateSubscriptionMessages.Any()) {
                    this.PersistProjectorCheckpoint(con);
                }
            }
        }
    }
}
