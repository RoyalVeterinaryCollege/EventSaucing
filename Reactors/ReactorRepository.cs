﻿using Autofac;
using Dapper;
using EventSaucing.Reactors.Messages;
using EventSaucing.Storage;
using NEventStore.Persistence.Sql;
using Newtonsoft.Json;
using Scalesque;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    public class ReactorRepository : IReactorRepository {
        private readonly IDbService dbService;
        private readonly IComponentContext container;
        private readonly IReactorBucketFacade reactorBucketFacade;
        private readonly IStreamIdHasher streamHasher;

        public ReactorRepository(IDbService dbService, IComponentContext container, IReactorBucketFacade reactorBucketFacade) {
            this.dbService = dbService;
            this.container = container;
            this.reactorBucketFacade = reactorBucketFacade;
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
                    //persist and get any subscriptions which need to be notified
                    IEnumerable<PreArticlePublishedMsg> preArticlePublishMessages = await con.QueryAsync<PreArticlePublishedMsg>(sb.ToString(), args);
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
       
        public async Task<(IReactor, PreviouslyPersistedPubSubData) > LoadFromDbAsync(long reactorId) {
            using (var con = dbService.GetConnection()) {
                await con.OpenAsync();

                //get all objects in one round trip
                const string sql = @"
SELECT VersionNumber, StateSerialisation, ReactorType, StateType FROM dbo.Reactors WHERE Id = @ReactorId;
SELECT AggregateId, StreamRevision FROM dbo.ReactorAggregateSubscriptions WHERE ReactorId = @ReactorId;
SELECT Id, Name FROM dbo.ReactorSubscriptions WHERE SubscribingReactorId = @ReactorId;
SELECT * FROM dbo.ReactorPublications WHERE PublishingReactorId = @ReactorId;";
                var results = await con.QueryMultipleAsync(sql, new { reactorId });

                //load reactor
                PreReactor intermediary = await results.ReadSingleAsync<PreReactor>();
                Type reactorType = Type.GetType(intermediary.ReactorType, throwOnError: true);
                Type stateType = Type.GetType(intermediary.StateType, throwOnError: true);
                IReactor reactor = (IReactor)container.Resolve(reactorType);
                reactor.State = JsonConvert.DeserializeObject(intermediary.StateSerialisation, stateType);
                reactor.Id = reactorId.ToSome();
                reactor.VersionNumber = intermediary.VersionNumber;

                //load pubsub
                var previous = new PreviouslyPersistedPubSubData(
                   await results.ReadAsync<ReactorAggregateSubscription>(),
                   await results.ReadAsync<ReactorSubscription>(),
                   (await results.ReadAsync<PreReactorPublication>()).Select(x=>x.ToReactorPublication())
                );

                return (reactor, previous);
            }
        }
    }
}
