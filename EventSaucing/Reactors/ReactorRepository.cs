using Autofac;
using Dapper;
using EventSaucing.Reactors.Messages;
using EventSaucing.Storage;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<ReactorRepository> logger;
        private readonly IStreamIdHasher streamHasher;

        public ReactorRepository(IDbService dbService, IComponentContext container, IReactorBucketFacade reactorBucketFacade, ILogger<ReactorRepository> logger) {
            this.dbService = dbService;
            this.container = container;
            this.reactorBucketFacade = reactorBucketFacade;
            this.logger = logger;
            this.streamHasher = new Sha1StreamIdHasher();
        }

        public async Task CreateReactorTablesAsync() {
            //create the reactor tables if necessary
            using (var con = dbService.GetConnection()) {
                await con.OpenAsync();
                await con.ExecuteAsync(SqlCreateReactorTables);
            }
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
                var msg = new ArticlePublished(
                           SubscribingReactorBucket,
                           Name,
                           SubscribingReactorId,
                           PublishingReactorId,
                           VersionNumber,
                           SubscriptionId,
                           PublicationId,
                           ArticleSerialisationType,
                           ArticleSerialisation
                       );
                return msg;
            }
        }
        private async Task<IEnumerable<Messages.ArticlePublished>> PersistAsync(UnitOfWork uow) {
            if (uow.Reactor.State is null) throw new ReactorValidationException($"Can't persist a reactor {uow.Reactor.GetType().FullName} if its State property is null");

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
       
        public async Task<(IReactor, PersistedPubSubData)> LoadFromDbAsync(long reactorId) {
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
    dbo.ReactorSubscriptions RS 

    INNER JOIN dbo.ReactorPublicationDeliveries RPD 
        ON RS.Id = RPD.SubscriptionId
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

        /// <summary>
        /// Sql to create the reactor persistence tables.  Idempotent.
        /// </summary>
        const string SqlCreateReactorTables = @"
SET XACT_ABORT ON
SET IMPLICIT_TRANSACTIONS ON

/****** Object:  Table [dbo].[Reactors]    Script Date: 20/11/2020 13:57:34 ******/
SET ANSI_NULLS ON
;

SET QUOTED_IDENTIFIER ON
;

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Reactors]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Reactors](
	[Bucket] [nvarchar](255) NOT NULL,
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[FactoryId] [int] NULL,
	[ReactorType] [nvarchar](max) NOT NULL,
	[StateType] [nvarchar](max) NOT NULL,
	[StateSerialisation] [nvarchar](max) NOT NULL,
	[VersionNumber] [int] NOT NULL,
	[RowVersion] [timestamp] NOT NULL,
 CONSTRAINT [PK_Reactors] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, FILLFACTOR = 95) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
;

/****** Object:  Table [dbo].[ReactorSubscriptions]    Script Date: 20/11/2020 13:57:34 ******/
SET ANSI_NULLS ON
;

SET QUOTED_IDENTIFIER ON
;

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ReactorSubscriptions]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ReactorSubscriptions](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[SubscribingReactorId] [bigint] NOT NULL,
	[Name] [varchar](2048) NOT NULL,
	[NameHash] [int] NOT NULL,
 CONSTRAINT [PK_ReactorSubscriptions] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
;

/****** Object:  Table [dbo].[ReactorPublications]    Script Date: 20/11/2020 13:57:34 ******/
SET ANSI_NULLS ON
;

SET QUOTED_IDENTIFIER ON
;

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ReactorPublications]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ReactorPublications](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](2048) NOT NULL,
	[PublishingReactorId] [bigint] NOT NULL,
	[NameHash] [int] NOT NULL,
	[ArticleSerialisationType] [nvarchar](max) NOT NULL,
	[ArticleSerialisation] [nvarchar](max) NOT NULL,
	[VersionNumber] [int] NOT NULL,
	[LastPublishedDate] [datetime] NOT NULL,
 CONSTRAINT [PK_ReactorPublications] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, FILLFACTOR = 95) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
;

/****** Object:  Table [dbo].[ReactorPublicationDeliveries]    Script Date: 20/11/2020 13:57:34 ******/
SET ANSI_NULLS ON
;

SET QUOTED_IDENTIFIER ON
;

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ReactorPublicationDeliveries]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ReactorPublicationDeliveries](
	[SubscriptionId] [bigint] NOT NULL,
	[PublicationId] [bigint] NOT NULL,
	[VersionNumber] [int] NOT NULL,
	[LastDeliveryDate] [datetime] NOT NULL,
 CONSTRAINT [PK_ReactorPublicationDeliveries] PRIMARY KEY CLUSTERED 
(
	[SubscriptionId] ASC,
	[PublicationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
;

SET ANSI_NULLS ON;

SET QUOTED_IDENTIFIER ON;
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ReactorsRoyalMailStatus]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ReactorsRoyalMailStatus](
	[Bucket] [nvarchar](255) NOT NULL,
	[LastCheckpointToken] [bigint] NOT NULL,
 CONSTRAINT [PK_ReactorsRoyalMailStatus] PRIMARY KEY CLUSTERED 
(
	[Bucket] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END;

/****** Object:  Index [IX_ReactorSubscriptions]    Script Date: 20/11/2020 13:57:34 ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ReactorSubscriptions]') AND name = N'IX_ReactorSubscriptions')
CREATE NONCLUSTERED INDEX [IX_ReactorSubscriptions] ON [dbo].[ReactorSubscriptions]
(
	[NameHash] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
;

/****** Object:  Index [IX_ReactorSubscriptions_1]    Script Date: 20/11/2020 13:57:34 ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ReactorSubscriptions]') AND name = N'IX_ReactorSubscriptions_1')
CREATE NONCLUSTERED INDEX [IX_ReactorSubscriptions_1] ON [dbo].[ReactorSubscriptions]
(
	[SubscribingReactorId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
;

/****** Object:  Index [IX_ReactorPublications]    Script Date: 20/11/2020 13:57:34 ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ReactorPublications]') AND name = N'IX_ReactorPublications')
CREATE NONCLUSTERED INDEX [IX_ReactorPublications] ON [dbo].[ReactorPublications]
(
	[NameHash] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
;

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_has_subscriptions]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorSubscriptions]'))
ALTER TABLE [dbo].[ReactorSubscriptions]  WITH CHECK ADD  CONSTRAINT [FK_has_subscriptions] FOREIGN KEY([SubscribingReactorId])
REFERENCES [dbo].[Reactors] ([Id])
ON UPDATE CASCADE
ON DELETE CASCADE
;

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_has_subscriptions]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorSubscriptions]'))
ALTER TABLE [dbo].[ReactorSubscriptions] CHECK CONSTRAINT [FK_has_subscriptions]
;

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_publication_of]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorPublications]'))
ALTER TABLE [dbo].[ReactorPublications]  WITH CHECK ADD  CONSTRAINT [FK_publication_of] FOREIGN KEY([PublishingReactorId])
REFERENCES [dbo].[Reactors] ([Id])
ON UPDATE CASCADE
ON DELETE CASCADE
;

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_publication_of]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorPublications]'))
ALTER TABLE [dbo].[ReactorPublications] CHECK CONSTRAINT [FK_publication_of]
;

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_has_delivery]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorPublicationDeliveries]'))
ALTER TABLE [dbo].[ReactorPublicationDeliveries]  WITH CHECK ADD  CONSTRAINT [FK_has_delivery] FOREIGN KEY([PublicationId])
REFERENCES [dbo].[ReactorPublications] ([Id])
;

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_has_delivery]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorPublicationDeliveries]'))
ALTER TABLE [dbo].[ReactorPublicationDeliveries] CHECK CONSTRAINT [FK_has_delivery]
;

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_was_delivered]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorPublicationDeliveries]'))
ALTER TABLE [dbo].[ReactorPublicationDeliveries]  WITH CHECK ADD  CONSTRAINT [FK_was_delivered] FOREIGN KEY([SubscriptionId])
REFERENCES [dbo].[ReactorSubscriptions] ([Id])
ON UPDATE CASCADE
ON DELETE CASCADE
;

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_was_delivered]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorPublicationDeliveries]'))
ALTER TABLE [dbo].[ReactorPublicationDeliveries] CHECK CONSTRAINT [FK_was_delivered]
;


IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ReactorAggregateSubscriptions]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ReactorAggregateSubscriptions](
	[StreamId] [char](40) NOT NULL,
	[ReactorId] [bigint] NOT NULL,
	[AggregateId] [uniqueidentifier] NOT NULL,
	[StreamRevision] [int] NULL,
 CONSTRAINT [PK_ReactorAggregateSubscriptions] PRIMARY KEY CLUSTERED 
(
	[StreamId] ASC,
	[ReactorId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
;

/****** Object:  Index [IX_ReactorAggregateSubscriptions]    Script Date: 20/11/2020 13:58:19 ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ReactorAggregateSubscriptions]') AND name = N'IX_ReactorAggregateSubscriptions')
CREATE NONCLUSTERED INDEX [IX_ReactorAggregateSubscriptions] ON [dbo].[ReactorAggregateSubscriptions]
(
	[AggregateId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
;

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_has_aggregate_subscription]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorAggregateSubscriptions]'))
ALTER TABLE [dbo].[ReactorAggregateSubscriptions]  WITH CHECK ADD  CONSTRAINT [FK_has_aggregate_subscription] FOREIGN KEY([ReactorId])
REFERENCES [dbo].[Reactors] ([Id])
ON UPDATE CASCADE
ON DELETE CASCADE
;

IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_has_aggregate_subscription]') AND parent_object_id = OBJECT_ID(N'[dbo].[ReactorAggregateSubscriptions]'))
ALTER TABLE [dbo].[ReactorAggregateSubscriptions] CHECK CONSTRAINT [FK_has_aggregate_subscription]
;



COMMIT";
    }
}
