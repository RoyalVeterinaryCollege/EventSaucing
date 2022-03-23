using Akka.Actor;
using Dapper;
using EventSaucing.Storage;
using Microsoft.Extensions.Logging;
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
    public class RoyalMail : ReceiveActor, IWithTimers {
        private readonly IDbService _dbservice;
        private readonly IReactorBucketFacade _reactorBucketRouter;
        private readonly ILogger _logger;
        private readonly IConfiguration _config;
        private readonly Random _rnd;
        private readonly string _bucket;

        //todo : how many instances of royal mail should there be per cluster?  it was designed as a singleton.

        /// <summary>
        /// Gets or sets the last checkpoint RoyalMail checked for outstanding aggregate subscriptions
        /// </summary>
        public long Checkpoint { get; set; } = 0L;

        public RoyalMail(IDbService dbservice, IReactorBucketFacade reactorBucketRouter, ILogger logger,
            IConfiguration config) {
            this._dbservice = dbservice;
            this._reactorBucketRouter = reactorBucketRouter;
            this._logger = logger;
            _config = config;
            _bucket = config.GetLocalBucketName();

            ReceiveAsync<Messages.PollForOutstandingArticles>(OnPollAsync);
            _rnd = new Random();
        }

        private class PreSubscribedAggregateChanged {
            public long ReactorId { get; set; }
            public Guid AggregateId { get; set; }
            public int StreamRevision { get; set; }

            public Reactors.Messages.SubscribedAggregateChanged ToMessage(string bucket) =>
                new Reactors.Messages.SubscribedAggregateChanged(bucket, ReactorId, AggregateId, StreamRevision);
        }

        protected override void PreStart() {
            base.PreStart();
            InitialisePersistedCheckpoint();

            // start a timer for polling
            Timers.StartPeriodicTimer(
                "poll",
                new Messages.PollForOutstandingArticles(),
                TimeSpan.FromSeconds(_config.GetValue<int?>("EventSaucing:RoyalMail:StartupDelay") ?? 5),
                TimeSpan.FromSeconds(_config.GetValue<int?>("EventSaucing:RoyalMail:PollingInterval") ?? 5)
                );
        }

        private async Task OnPollAsync(Messages.PollForOutstandingArticles arg) {
            await PublishAggregateSubscriptionsMessages();
            await PublishArticleSubscriptionMessages();
        }

        /// <summary>
        /// Finds reactors that are subscribed to aggregates and sends SubscribedAggregateChanged message if required
        /// </summary>
        /// <returns></returns>
        private async Task PublishAggregateSubscriptionsMessages() {
            using (var con = _dbservice.GetReplica()) {
                await con.OpenAsync();
                int maxSubscriptions = _config.GetValue<int?>("EventSaucing:RoyalMail:MaxNumberSubscriptions") ?? 100;

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
                var aggregateSubscriptionsResults = await con.QueryMultipleAsync(sqlAggregateSubscriptions,
                    new { bucket = _bucket, maxSubscriptions, CurrentCheckpoint = Checkpoint });

                //Look for aggregate subscriptions that need to be updated in our bucket
                // var aggregateSubscriptionMessages = (await con.QueryAsync<PreSubscribedAggregateChanged>(sqlAggregateSubscriptions, new {bucket=_bucket, maxSubscriptions})).ToList();
                var aggregateSubscriptionMessages =
                    (await aggregateSubscriptionsResults.ReadAsync<PreSubscribedAggregateChanged>()).ToList();


                if (aggregateSubscriptionMessages.Any()) {
                    _logger.LogDebug(
                        $"Found {aggregateSubscriptionMessages.Count} aggregate subscriptions for bucket {_bucket}");
                }
                else {
                    _logger.LogDebug($"No aggregate subscriptions for bucket {_bucket} need to be updated");
                }

                foreach (var preMsg in aggregateSubscriptionMessages.Shuffle(_rnd)) {
                    await _reactorBucketRouter.TellAsync(preMsg.ToMessage(_bucket));
                }

                // update persisted checkpoint
                Checkpoint = (await aggregateSubscriptionsResults.ReadAsync<long>()).First();
                await con.ExecuteAsync(
                    "UPDATE dbo.ReactorsRoyalMailStatus SET LastCheckpointToken = @CheckpointNumber WHERE Bucket=@Bucket",
                    new { Bucket = _bucket, Checkpoint });
            }
        }

        /// <summary>
        /// Finds reactors that are subscribed to articles and sends ArticlePublished message if required
        /// </summary>
        /// <returns></returns>
        private async Task PublishArticleSubscriptionMessages() {
            // max number of subscriptions to update in one poll
            int maxSubscriptions = _config.GetValue<int?>("EventSaucing:RoyalMail:MaxNumberSubscriptions") ?? 100;

            using (var con = _dbservice.GetReplica()) {
                await con.OpenAsync();

                //Look for article subscriptions that need to be updated
                const string sqlReactorSubscriptions = @"
SELECT TOP (@MaxSubscriptions) 
    R.Bucket AS [ReactorBucket],
    RP.Name, 
    RP.Id AS [PublicationId],
    RS.SubscribingReactorId, 
    RS.Id as SubscriptionId, 
    RP.PublishingReactorId, 
    RP.VersionNumber, 
    RP.ArticleSerialisationType,
    RP.ArticleSerialisation

FROM dbo.ReactorSubscriptions RS

INNER JOIN dbo.ReactorPublications RP
	ON RS.NameHash = RP.NameHash
	AND RS.Name = RP.Name

LEFT JOIN dbo.ReactorPublicationDeliveries RPD
	ON RS.Id = RPD.SubscriptionId
	AND RP.Id = RPD.PublicationId

-- Without this lock hint, RoyalMail can deadlock with UoW's reactor persistence code.  It's safe to read dirty data here because we are only joining to get the Reactor Bucket and this never changes
INNER JOIN dbo.Reactors R WITH(READUNCOMMITTED)
    ON RS.SubscribingReactorId = R.Id

WHERE 
    R.Bucket = @Bucket
	AND RPD.SubscriptionId IS NULL --never delivered
	OR (RPD.VersionNumber < RP.VersionNumber); --OR there is a new version";

                var messages =
                    (await con.QueryAsync<ArticlePublished>(sqlReactorSubscriptions,
                        new { Bucket = _bucket, maxSubscriptions })).ToList();

                if (messages.Any()) {
                    _logger.LogDebug($"Found {messages.Count} article subscriptions for bucket {_bucket}");
                }
                else {
                    _logger.LogDebug($"No article subscriptions for bucket {_bucket} need to be updated");
                }

                foreach (var message in messages.Shuffle(_rnd)) {
                    await _reactorBucketRouter.TellAsync(message);
                }
            }
        }

        private void InitialisePersistedCheckpoint() {
            // todo remove 'my bucket' concept from royalmail
            // todo move this row to projector table?
            using (var con = _dbservice.GetReplica()) {
                con.Open();

                var results = con.Query<long>(
                    "SELECT LastCheckPointToken FROM dbo.ReactorsRoyalMailStatus WHERE Bucket = @Bucket",
                    new { _bucket }
                );

                results.ForEach(x => { Checkpoint = x; });

                // persist checkpoint if necessary, so there will always be a row after this point
                const string sqlUpdateCheckpoint = @"
INSERT INTO [dbo].[ReactorsRoyalMailStatus]
   ([Bucket]
   ,[LastCheckpointToken])
SELECT
   @Bucket
   ,@LastCheckpointToken
WHERE NOT EXISTS(SELECT 1 FROM dbo.ReactorsRoyalMailStatus WHERE Bucket = @Bucket)";
                con.Execute(sqlUpdateCheckpoint,
                    new { Bucket = _bucket, LastCheckpointToken = Checkpoint }
                );
            }
        }

        /// <summary>
        /// RoyalMail's messages
        /// </summary>
        public class Messages {
            /// <summary>
            /// Message instructs <see cref="RoyalMail"/> to poll db to check for reactor subscriptions with new articles or for aggregates with new commits
            /// </summary>
            public class PollForOutstandingArticles { }
        }

        public ITimerScheduler Timers { get; set; }
    }
}