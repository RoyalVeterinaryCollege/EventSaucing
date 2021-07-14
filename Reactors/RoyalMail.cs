using Akka.Actor;
using Dapper;
using EventSaucing.Storage;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    /// <summary>
    /// Actor responsible for polling the db looking for subscribers with outstanding articles or aggregate events.  
    /// 
    /// Existing subscribers are messaged immediately when a publisher creates a new version of an article but newly created subscriptions don't receive any pre-existing publications immediately. They are messaged by RoyalMail.
    /// </summary>
    public class RoyalMail : ReceiveActor {
        private readonly IDbService dbservice;
        private readonly IReactorBucketFacade reactorBucketRouter;
        private readonly ILogger<RoyalMail> logger;
        private readonly Random rnd;

        public RoyalMail(IDbService dbservice, IReactorBucketFacade reactorBucketRouter, ILogger<RoyalMail> logger) {
            this.dbservice = dbservice;
            this.reactorBucketRouter = reactorBucketRouter;
            this.logger = logger;
            ReceiveAsync<LocalMessages.PollForOutstandingArticles>(OnPollAsync);
            rnd = new Random();
        }
        private class PreSubscribedAggregateChanged {
            public string ReactorBucket { get; set; }
            public long ReactorId { get; set; }
            public Guid AggregateId { get; set; }
            public int StreamRevision { get; set; }
            public Messages.SubscribedAggregateChanged ToMessage() => new Messages.SubscribedAggregateChanged(ReactorBucket, ReactorId, AggregateId, StreamRevision);
        }
        private async Task OnPollAsync(LocalMessages.PollForOutstandingArticles arg) {
            using (var con = dbservice.GetConnection()) {
                await con.OpenAsync();

                //todo Need to limit the numbers of RoyalMail messages via config rather than hardcode 100
                const string sqlAggregateSubscriptions = @"
SELECT TOP 100 R.Bucket AS ReactorBucket, RS.ReactorId, RS.AggregateId, MAX(C.StreamRevision) StreamRevision
FROM 
    [dbo].[ReactorAggregateSubscriptions] RS 
    INNER JOIN dbo.Commits C
        ON RS.StreamId = C.StreamId
        AND C.StreamRevision > RS.StreamRevision
        AND C.BucketId='default'

    -- Without this lock hint, RoyalMail can deadlock with UoW's reactor persistance code.  It's safe to read dirty data here because we are only joining to get the Reactor Bucket and this never changes
    INNER JOIN dbo.Reactors R WITH(READUNCOMMITTED)
        ON RS.ReactorId = R.Id
GROUP BY
    R.Bucket, RS.ReactorId, RS.AggregateId;";
                
                //Look for aggregate subscriptions that need to be updated
                var aggregateSubscriptionMessages = (await con.QueryAsync<PreSubscribedAggregateChanged>(sqlAggregateSubscriptions)).ToList();

                if (aggregateSubscriptionMessages.Any()) {
                    var bucketcounts =
                      aggregateSubscriptionMessages
                      .GroupBy(x => x.ReactorBucket)
                      .Select(x => $"'{x.Key}' {x.Count()} messages");
                    logger.LogInformation($"Found aggregate subscriptions for the following buckets: {string.Join(",", bucketcounts)}");
                } else {
                    logger.LogInformation($"No aggregate subscriptions need to be updated");
                }

                foreach (var preMsg in aggregateSubscriptionMessages.Shuffle(rnd)) {
                    reactorBucketRouter.Tell(preMsg.ToMessage());
                }

                //todo Need to limit the numbers of RoyalMail messages via config rather than hardcode 100
                //Look for article subscriptions that need to be updated
                const string sqlReactorSubscriptions = @"
SELECT TOP 100 R.Bucket AS SubscribingReactorBucket, RP.Name,  RP.Id AS [PublicationId], RS.SubscribingReactorId, RS.Id as SubscriptionId, RP.PublishingReactorId, RP.VersionNumber, RP.ArticleSerialisationType, RP.ArticleSerialisation

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
	RPD.SubscriptionId IS NULL --never delivered
	OR (RPD.VersionNumber < RP.VersionNumber); --OR there is a new version";

                var preMessages = await con.QueryAsync<PreArticlePublished>(sqlReactorSubscriptions);

                if (preMessages.Any()) {
                    var bucketcounts =
                      preMessages
                      .GroupBy(x => x.SubscribingReactorBucket)
                      .Select(x => $"'{x.Key}' {x.Count()} messages");
                    logger.LogInformation($"Found article subscriptions for the following buckets: {string.Join(",", bucketcounts)}");
                } else {
                    logger.LogInformation($"No article subscriptions need to be updated");
                }

                foreach (var preMsg in preMessages.Shuffle(rnd)) {
                    reactorBucketRouter.Tell(preMsg.ToMessage());
                }
            }
        }

        private class PreArticlePublished {
            public string SubscribingReactorBucket { get; set; }
            public string Name { get; set; }
            public string ArticleSerialisationType { get; set; }
            public string ArticleSerialisation { get; set; }
            public long SubscribingReactorId { get; set; }
            public long PublishingReactorId { get; set; }
            public int VersionNumber { get; set; }
            public long SubscriptionId { get; set; } 
            public long PublicationId { get; set; } 

            public Messages.ArticlePublished ToMessage() {
                return new Messages.ArticlePublished(
                    SubscribingReactorBucket,
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
                TimeSpan.FromSeconds(5), // on start up, wait this long
                TimeSpan.FromSeconds(5), // todo polling interval for royal mail to come from config
                Self, new LocalMessages.PollForOutstandingArticles(), 
                ActorRefs.NoSender);
        }
        public class LocalMessages {
            /// <summary>
            /// Message instructs royalmail to poll db to check for reactor subscriptions with new articles or for aggregates with new commits
            /// </summary>
            public class PollForOutstandingArticles { }
        }

    }
}
