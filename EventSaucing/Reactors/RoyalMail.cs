using Akka.Actor;
using Dapper;
using EventSaucing.Storage;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;
using EventSaucing.Reactors.Messages;
using Microsoft.Extensions.Configuration;
using Scalesque;

namespace EventSaucing.Reactors {
    /// <summary>
    /// Actor responsible for polling the db looking for subscribers with outstanding articles or aggregate events.  
    /// 
    /// Existing subscribers are messaged immediately when a publisher creates a new version of an article but newly created subscriptions don't receive any pre-existing publications immediately. They are messaged by RoyalMail.
    /// </summary>
    public class RoyalMail : ReceiveActor {
        private readonly IDbService _dbservice;
        private readonly IReactorBucketFacade _reactorBucketRouter;
        private readonly ILogger<RoyalMail> _logger;
        private readonly IConfiguration _config;
        private readonly Random _rnd;
        private readonly string _bucket;

        //todo : how many instances of royal mail should there be per cluster?  it was designed as a singleton.

        /// <summary>
        /// Gets or sets the last checkpoint RoyalMail checked for outstanding aggregate subscriptions
        /// </summary>
        public Option<long> Checkpoint { get; set; } = Option.None();
        public RoyalMail(IDbService dbservice, IReactorBucketFacade reactorBucketRouter, ILogger<RoyalMail> logger, IConfiguration config) {
            this._dbservice = dbservice;
            this._reactorBucketRouter = reactorBucketRouter;
            this._logger = logger;
            _config = config;
            _bucket = config.GetLocalBucketName();

            ReceiveAsync<LocalMessages.PollForOutstandingArticles>(OnPollAsync);
            _rnd = new Random();
        }
        private class PreSubscribedAggregateChanged {
            public long ReactorId { get; set; }
            public Guid AggregateId { get; set; }
            public int StreamRevision { get; set; }
            public Messages.SubscribedAggregateChanged ToMessage(string bucket) => new Messages.SubscribedAggregateChanged(bucket, ReactorId, AggregateId, StreamRevision);
        }
        private async Task OnPollAsync(LocalMessages.PollForOutstandingArticles arg) {
            // max number of subscriptions to update in one poll
            int maxSubscriptions = _config.GetValue<int?>("EventSaucing:RoyalMail:MaxNumberSubscriptions") ?? 100;

            using (var con = _dbservice.GetConnection()) {
                await con.OpenAsync();

                // see if checkpoint needs to be defaulted or initialised from persisted value in db
                if (Checkpoint.IsEmpty) {
                    Checkpoint = 0L.ToSome(); //default to before start of commit store which begins at 1

                    var results = con.Query<long>("SELECT LastCheckPointToken FROM dbo.ReactorsRoyalMailStatus WHERE Bucket = @Bucket", new { _bucket });

                    results.ForEach(x => {
                        Checkpoint = x.ToSome();
                    });

                    //initialise checkpoint in db if necessary, so there will always be a row after this point
                    const string sqlUpdateCheckpoint = @"
INSERT INTO [dbo].[ReactorsRoyalMailStatus]
   ([Bucket]
   ,[LastCheckpointToken])
SELECT
   @Bucket
   ,@LastCheckpointToken
WHERE NOT EXISTS(SELECT 1 FROM dbo.ReactorsRoyalMailStatus WHERE Bucket = @Bucket)";
                    await con.ExecuteAsync(sqlUpdateCheckpoint,new {Bucket = _bucket, LastCheckpointToken = Checkpoint.Get()});
                }

                const string sqlAggregateSubscriptions = @"
--determine the lastest CheckpointNumber, we will ignore commits made after this during this poll
DECLARE @LatestCheckpointNumber bigint

SELECT @LatestCheckpointNumber = MAX(CheckpointNumber) FROM dbo.Commits;

SELECT TOP (@MaxSubscriptions) RS.ReactorId, RS.AggregateId, MAX(C.StreamRevision) StreamRevision
FROM 
    [dbo].[ReactorAggregateSubscriptions] RS 
INNER JOIN dbo.Commits C
    ON RS.StreamId = C.StreamId
    AND C.StreamRevision > RS.StreamRevision
    AND C.BucketId='default'
    --don't include commits we have already processed
    AND C.CheckpointNumber > @CurrentCheckpoint
    --look no further than LatestCheckpointNumber
    AND C.CheckpointNumber <= @LatestCheckpointNumber
    

-- Without this lock hint, RoyalMail can deadlock with UoW's reactor persistence code.  It's safe to read dirty data here because we are only joining to get the Reactor Bucket and this never changes
INNER JOIN dbo.Reactors R WITH(READUNCOMMITTED)
    ON RS.ReactorId = R.Id
WHERE
    R.Bucket = @Bucket
GROUP BY
   RS.ReactorId, RS.AggregateId;

SELECT @LatestCheckpointNumber;
";
                var aggregateSubscriptionsResults = await con.QueryMultipleAsync(sqlAggregateSubscriptions, new { bucket = _bucket, maxSubscriptions, CurrentCheckpoint = Checkpoint.Get() });

                //Look for aggregate subscriptions that need to be updated in our bucket
                // var aggregateSubscriptionMessages = (await con.QueryAsync<PreSubscribedAggregateChanged>(sqlAggregateSubscriptions, new {bucket=_bucket, maxSubscriptions})).ToList();
                var aggregateSubscriptionMessages = (await aggregateSubscriptionsResults.ReadAsync<PreSubscribedAggregateChanged>()).ToList();


                if (aggregateSubscriptionMessages.Any()) {
                    _logger.LogInformation($"Found {aggregateSubscriptionMessages.Count} aggregate subscriptions for bucket {_bucket}");
                } else {
                    _logger.LogInformation($"No aggregate subscriptions for bucket {_bucket} need to be updated");
                }

                foreach (var preMsg in aggregateSubscriptionMessages.Shuffle(_rnd)) {
                    _reactorBucketRouter.Tell(preMsg.ToMessage(_bucket));
                }

                // update persisted checkpoint
                Checkpoint = (await aggregateSubscriptionsResults.ReadAsync<long>()).First().ToSome();
                await con.ExecuteAsync("UPDATE dbo.ReactorsRoyalMailStatus SET LastCheckpointToken = @CheckpointNumber WHERE Bucket=@Bucket", new {Bucket=_bucket, Checkpoint = Checkpoint.Get()});


                //Look for article subscriptions that need to be updated
                const string sqlReactorSubscriptions = @"
SELECT TOP (@MaxSubscriptions) RP.Name,  RP.Id AS [PublicationId], RS.SubscribingReactorId, RS.Id as SubscriptionId, RP.PublishingReactorId, RP.VersionNumber, RP.ArticleSerialisationType, RP.ArticleSerialisation

FROM dbo.ReactorSubscriptions RS

INNER JOIN dbo.ReactorPublications RP
	ON RS.NameHash = RP.NameHash
	AND RS.Name = RP.Name

LEFT JOIN dbo.ReactorPublicationDeliveries RPD
	ON RS.Id = RPD.SubscriptionId
	AND RP.Id = RPD.PublicationId

-- Without this lock hint, RoyalMail can deadlock with UoW's reactor persistance code.  It's safe to read dirty data here because we are only joining to get the Reactor Bucket and this never changes
INNER JOIN dbo.Reactors R WITH(READUNCOMMITTED)
    ON RS.SubscribingReactorId = R.Id

WHERE 
    R.Bucket = @Bucket
	AND RPD.SubscriptionId IS NULL --never delivered
	OR (RPD.VersionNumber < RP.VersionNumber); --OR there is a new version";

                var preMessages = await con.QueryAsync<PreArticlePublished>(sqlReactorSubscriptions, new {Bucket=_bucket, maxSubscriptions });

                if (preMessages.Any()) {
                    _logger.LogInformation($"Found {aggregateSubscriptionMessages.Count} article subscriptions for bucket {_bucket}");
                } else {
                    _logger.LogInformation($"No article subscriptions for bucket {_bucket} need to be updated");
                }

                foreach (var preMsg in preMessages.Shuffle(_rnd)) {
                    _reactorBucketRouter.Tell(preMsg.ToMessage(_bucket));
                }
            }
        }

        private class PreArticlePublished {
            public string Name { get; set; }
            public string ArticleSerialisationType { get; set; }
            public string ArticleSerialisation { get; set; }
            public long SubscribingReactorId { get; set; }
            public long PublishingReactorId { get; set; }
            public int VersionNumber { get; set; }
            public long SubscriptionId { get; set; } 
            public long PublicationId { get; set; } 

            public ArticlePublished ToMessage(string subscribingReactorBucket) {
                return new Messages.ArticlePublished(
                    subscribingReactorBucket,
                    Name, 
                    SubscribingReactorId,
                    PublishingReactorId,
                    VersionNumber,
                    SubscriptionId,
                    PublicationId,
                    JsonConvert.DeserializeObject(ArticleSerialisation, Type.GetType(ArticleSerialisationType, throwOnError: true))
                );
            }
        }

        /// <summary>
        /// RoyalMail's messages
        /// </summary>
        public class LocalMessages {
            /// <summary>
            /// Message instructs royalmail to poll db to check for reactor subscriptions with new articles or for aggregates with new commits
            /// </summary>
            public class PollForOutstandingArticles { }
        }

    }
}
