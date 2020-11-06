using Akka.Actor;
using Dapper;
using EventSaucing.Storage;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    /// <summary>
    /// Actor responsible for polling the db looking for subscribers with outstanding articles
    /// </summary>
    public class RoyalMail : ReceiveActor {
        private readonly IDbService dbservice;

        public RoyalMail(IDbService dbservice) {
            this.dbservice = dbservice;

            ReceiveAsync<LocalMessages.PollForOutstandingArticles>(OnPollAsync);
        }

        private async Task OnPollAsync(LocalMessages.PollForOutstandingArticles arg) {
            using (var con = dbservice.GetConnection()) {
                await con.OpenAsync();

                // todo restrict the number of subscriptions to get in one go?
                const string sqlAggregateSubscriptions = @"
SELECT RS.ReactorId, RS.AggregateId, MAX(C.StreamRevision) StreamRevision
FROM 
    [dbo].[ReactorAggregateSubscriptions] RS 
    INNER JOIN dbo.Commits C
        ON RS.StreamId = C.StreamId
        AND C.StreamRevision > RS.StreamRevision
        AND C.BucketId='default'
GROUP BY
    RS.ReactorId, RS.AggregateId;";
                //dapper produces the message directly from the db
                var aggregateSubscriptionMessages = await con.QueryAsync<ReactorActor.LocalMessages.SubscribedAggregateChanged>(sqlAggregateSubscriptions);
                ActorSelection reactorActor = Context.ActorSelection("../reactor-actors"); //parent is reactor-supervisor

                foreach (var msg in aggregateSubscriptionMessages) {
                    reactorActor.Tell(msg);
                }

                //todo royalmail to check for outstanding reactor subsciptions as well
                const string sqlReactorSubscriptions = @"
SELECT RP.Id AS [PublicationId], RS.SubscribingReactorId, RS.Id as SubscriptionId, RP.PublishingReactorId, RP.VersionNumber, RP.ArticleSerialisationType, RP.ArticleSerialisation

FROM dbo.ReactorSubscriptions RS

INNER JOIN dbo.ReactorPublications RP
	ON RS.NameHash = RP.NameHash
	AND RS.Name = RP.Name

LEFT JOIN dbo.ReactorPublicationDeliveries RPD
	ON RS.Id = RPD.SubscriptionId
	AND RP.Id = RPD.PublicationId

WHERE 
	RPD.SubscriptionId IS NULL --never delivered
	OR (RPD.VersionNumber < RP.VersionNumber); --OR there is a new version";

                var preMessages = await con.QueryAsync<PreArticlePublished>(sqlReactorSubscriptions);
                foreach (var preMsg in preMessages) {
                    try {
                        var msg = preMsg.ToMessage();
                        reactorActor.Tell(msg);
                    } catch (Exception e) {
                        throw;
                    }
                }
            }
        }

        private class PreArticlePublished {
            public string ArticleSerialisationType { get; set; }
            public string ArticleSerialisation { get; set; }
            public long SubscribingReactorId { get; set; }
            public long PublishingReactorId { get; set; }
            public int VersionNumber { get; set; }
            public long SubscriptionId { get; set; } 
            public long PublicationId { get; set; } 

            public ReactorActor.LocalMessages.ArticlePublished ToMessage() {
                return new ReactorActor.LocalMessages.ArticlePublished {
                    SubscribingReactorId = SubscribingReactorId,
                    PublishingReactorId = PublishingReactorId,
                    VersionNumber = VersionNumber,
                    SubscriptionId = SubscriptionId,
                    PublicationId = PublicationId,
                    Article = JsonConvert.DeserializeObject(ArticleSerialisation, Type.GetType(ArticleSerialisationType, throwOnError: true))
                };
            }
        }

        protected override void PreStart() {
            //schedule a poll message to be sent every n seconds
            Context.System.Scheduler.ScheduleTellRepeatedly(
                TimeSpan.FromSeconds(5), // on start up, wait this long
                TimeSpan.FromSeconds(5), // polling interval
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
