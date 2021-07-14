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

namespace EventSaucing.Reactors {
    /// <summary>
    /// Actor responsible for polling the db looking for subscribers with outstanding articles or aggregate events.  
    /// 
    /// Existing subscribers are messaged immediately when a publisher creates a new version of an article but newly created subscriptions don't receive any pre-existing publications immediately. They are messaged by RoyalMail.
    /// </summary>
    public class RoyalMail : ReceiveActor {
        private readonly IDbService dbservice;
        private readonly IReactorBucketFacade _reactorBucketRouter;
        private readonly ILogger<RoyalMail> _logger;
        private readonly IConfiguration _config;
        private readonly Random _rnd;
        private readonly string _bucket;

        public RoyalMail(IDbService dbservice, IReactorBucketFacade reactorBucketRouter, ILogger<RoyalMail> logger, IConfiguration config) {
            this.dbservice = dbservice;
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

            using (var con = dbservice.GetConnection()) {
                await con.OpenAsync();

                const string sqlAggregateSubscriptions = @"
SELECT TOP (@MaxSubscriptions) RS.ReactorId, RS.AggregateId, MAX(C.StreamRevision) StreamRevision
FROM 
    [dbo].[ReactorAggregateSubscriptions] RS 
INNER JOIN dbo.Commits C
    ON RS.StreamId = C.StreamId
    AND C.StreamRevision > RS.StreamRevision
    AND C.BucketId='default'

-- Without this lock hint, RoyalMail can deadlock with UoW's reactor persistence code.  It's safe to read dirty data here because we are only joining to get the Reactor Bucket and this never changes
INNER JOIN dbo.Reactors R WITH(READUNCOMMITTED)
    ON RS.ReactorId = R.Id
WHERE
    R.Bucket = @Bucket
GROUP BY
   RS.ReactorId, RS.AggregateId;";
                
                //Look for aggregate subscriptions that need to be updated in our bucket
                var aggregateSubscriptionMessages = (await con.QueryAsync<PreSubscribedAggregateChanged>(sqlAggregateSubscriptions, new {bucket=_bucket, maxSubscriptions})).ToList();

                if (aggregateSubscriptionMessages.Any()) {
                    _logger.LogInformation($"Found {aggregateSubscriptionMessages.Count} aggregate subscriptions for bucket {_bucket}");
                } else {
                    _logger.LogInformation($"No aggregate subscriptions for bucket {_bucket} need to be updated");
                }

                foreach (var preMsg in aggregateSubscriptionMessages.Shuffle(_rnd)) {
                    _reactorBucketRouter.Tell(preMsg.ToMessage(_bucket));
                }

                
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

        protected override void PreStart() {
            //schedule a poll message to be sent every n seconds
            Context.System.Scheduler.ScheduleTellRepeatedly(
                TimeSpan.FromSeconds(_config.GetValue<int?>("EventSaucing:RoyalMail:StartupDelay") ?? 5), // on start up, wait this long
                TimeSpan.FromSeconds(_config.GetValue<int?>("EventSaucing:RoyalMail:PollingInterval") ?? 5), // wait this long between polling
                Self, new LocalMessages.PollForOutstandingArticles(), 
                ActorRefs.NoSender);
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
