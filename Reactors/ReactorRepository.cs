using Autofac;
using Dapper;
using EventSaucing.Reactors.Messages;
using EventSaucing.Storage;
using Microsoft.Extensions.Logging;
using NEventStore.Persistence.Sql;
using Newtonsoft.Json;
using Scalesque;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    public class ReactorRepository : IReactorRepository {
        private readonly IDbService dbService;
        private readonly IComponentContext container;
        private readonly IReactorBucketFacade reactorBucketFacade;
        private readonly ILogger<ReactorRepository> logger;
        private readonly IStreamIdHasher streamHasher;

        public ReactorRepository(IDbService dbService, IComponentContext container, IReactorBucketFacade reactorBucketFacade, ILogger<ReactorRepository> logger) {
            this.dbService = dbService;
            this.container = container;
            this.reactorBucketFacade = reactorBucketFacade;
            this.logger = logger;
            this.streamHasher=new Sha1StreamIdHasher();
        }
     
        public IUnitOfWork Attach(IReactor reactor) {
            var uow = new UnitOfWork(streamHasher, reactorBucketFacade, reactor, Option.None(), PersistAsync);
            return uow;
        }

        public async Task<IUnitOfWork> LoadAsync(long reactorId) {
            var (reactor, previous) = await LoadFromDbAsync(reactorId);
            return new UnitOfWork(streamHasher, reactorBucketFacade, reactor, previous.ToSome(), PersistAsync);
        }
        private class PreArticlePublishedMsg {
            public string SubscribingReactorBucket { get; set; }
            public string Name { get; set; }
            public long SubscribingReactorId { get; set; }
            public long PublishingReactorId { get; set; }
            public int VersionNumber { get; set; }
            public string ArticleSerialisationType { get; set; }
            public string ArticleSerialisation { get; set; }
            public long SubscriptionId { get; set; } 
            public long PublicationId { get; set; }

            public ArticlePublished ToMessage() {
                if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentNullException("Name property was not set correctly during persistance");
                var type = Type.GetType(ArticleSerialisationType, throwOnError: true);
                return new Messages.ArticlePublished(
                           SubscribingReactorBucket,
                           Name,
                           SubscribingReactorId,
                           PublishingReactorId,
                           VersionNumber,
                           SubscriptionId,
                           PublicationId,
                           JsonConvert.DeserializeObject(ArticleSerialisation, type)
                       );
            }
        }
        private async Task<IEnumerable<Messages.ArticlePublished>> PersistAsync(UnitOfWork uow) {
            if (uow.Reactor.State is null) throw new ReactorValidationException($"Can't persist a reactor {uow.Reactor.GetType().FullName} if its State property is null");

            try {
                using (var con = dbService.GetConnection()) {
                    await con.OpenAsync();
                    var (sb, args) = uow.GetSQLAndArgs();

                    //persist and get any subscriptions which need to be notified + the reactorid in the case of a new reactor
                    var results = await con.QueryMultipleAsync(sb.ToString(), args);

                    //article messages to be published
                    IEnumerable<PreArticlePublishedMsg> preArticlePublishMessages = await results.ReadAsync<PreArticlePublishedMsg>();
                    //set the reactor id (will only make a difference for new reactors
                    uow.Reactor.Id = (await results.ReadFirstAsync<long>()).ToSome();

                    logger.LogDebug($"Found {preArticlePublishMessages.Count()} article subscriptions to be delivered after persisting reactor id {uow.Reactor.Id.Get()}");
                    return preArticlePublishMessages.Select(pre => pre.ToMessage());
                }
            } catch (Exception e) {

                throw;
            }
            
        }

        private class PreReactor {
            public int VersionNumber { get; set; }
            public string StateSerialisation { get; set; }
            public string ReactorType { get; set; }
            public string StateType { get; set; }
        }
        private class PreReactorPublication {
            public Option<long> Id { get; set; }
            public string Name { get; set; }
            public string ArticleSerialisationType { get; set; }
            public string ArticleSerialisation { get; set; }
            public int VersionNumber { get; set; }
            public DateTime LastPublishedDate { get; set; }

            public ReactorPublication ToReactorPublication() {
                Type articleType = Type.GetType(ArticleSerialisationType, throwOnError: true);
                var result =  new ReactorPublication {
                    Id = Id,
                    Name = Name,
                    VersionNumber = VersionNumber,
                    LastPublishedDate=LastPublishedDate
                };

                result.RestoreArticle(JsonConvert.DeserializeObject(ArticleSerialisation, articleType));
                return result;
            }
        }
       
        public async Task<(IReactor, PersistedPubSubData) > LoadFromDbAsync(long reactorId) {
            using (var con = dbService.GetConnection()) {
                await con.OpenAsync();

                //get all objects in one round trip
                const string sql = @"
SELECT VersionNumber, StateSerialisation, ReactorType, StateType FROM dbo.Reactors WHERE Id = @ReactorId;
SELECT AggregateId, StreamRevision FROM dbo.ReactorAggregateSubscriptions WHERE ReactorId = @ReactorId;
SELECT Id, Name FROM dbo.ReactorSubscriptions WHERE SubscribingReactorId = @ReactorId;
SELECT * FROM dbo.ReactorPublications WHERE PublishingReactorId = @ReactorId;
SELECT 
    RPD.* 
FROM 
    dbo.ReactorPublicationDeliveries RPD 

    INNER JOIN dbo.ReactorSubscriptions RS 
        ON RPD.SubscriptionId = RS.Id
WHERE
    RS.SubscribingReactorId=@ReactorId;";
                var results = await con.QueryMultipleAsync(sql, new { reactorId });

                //load reactor
                PreReactor intermediary = await results.ReadSingleAsync<PreReactor>();
                Type reactorType = Type.GetType(intermediary.ReactorType, throwOnError: true);
                Type stateType = Type.GetType(intermediary.StateType, throwOnError: true);
                IReactor reactor = (IReactor)container.Resolve(reactorType);
                reactor.State = JsonConvert.DeserializeObject(intermediary.StateSerialisation, stateType);
                reactor.Id = reactorId.ToSome();
                reactor.VersionNumber = intermediary.VersionNumber;

                //load history of pub/sub
                var previous = new PersistedPubSubData(
                   await results.ReadAsync<ReactorAggregateSubscription>(),
                   await results.ReadAsync<ReactorSubscription>(),
                   (await results.ReadAsync<PreReactorPublication>()).Select(x=>x.ToReactorPublication()),
                   await results.ReadAsync<ReactorPublicationDeliveries>()
                );

                return (reactor, previous);
            }
        }
    }
}
