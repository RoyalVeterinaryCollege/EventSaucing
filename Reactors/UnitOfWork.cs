﻿using NEventStore;
using NEventStore.Persistence.Sql;
using Newtonsoft.Json;
using Scalesque;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    /// <summary>
    /// Internal methods for tracking delivery of subscriptions to Reactors as part of the Unit Of Work
    /// </summary>
    internal interface IUnitOfWorkInternal : IUnitOfWork {
        /// <summary>
        /// Records delivery of an aggregate subscription
        /// </summary>
        /// <param name="subscription"></param>
        void RecordDelivery(ReactorAggregateSubscription subscription);

        /// <summary>
        /// Records delivery of a publication
        /// </summary>
        /// <param name="delivery"></param>
        void RecordDelivery(ReactorPublicationDelivery delivery);
    }
    /// <summary>
    /// Represents a unit of work on a reactor.  All requested work is persisted in a transaction so all parts of the UOW either suceceed or fail
    /// </summary>
    public interface IUnitOfWork { 
        /// <summary>
        /// The Reactor which is the subject of the Unit of Work pattern
        /// </summary>
        IReactor Reactor { get; }
        /// <summary>
        /// The previously persisted publication and subscription records for the reactor.  If None, the reactor has never been persisted.
        /// </summary>
        Option<PreviouslyPersistedPubSubData> Previous { get; }
        /// <summary>
        /// Subscribe this reactor to the aggregate as part of the UOW
        /// </summary>
        /// <param name="subscription"></param>
        void Subscribe(ReactorAggregateSubscription subscription);
        /// <summary>
        /// Subscribe this reactor to the named topic
        /// </summary>
        /// <param name="topic">The topic which the reactor subsctibes to</param>
        void Subscribe(string topic);
        /// <summary>
        /// Subscribes to the event stream
        /// </summary>
        /// <param name="stream"></param>
        void Subscribe(Guid aggregateId, IEventStream stream);
        /// <summary>
        /// Publish an article for a publication
        /// </summary>
        /// <param name="name"></param>
        /// <param name="article"></param>
        void Publish(string name, object article);
        /// <summary>
        /// Attempt to complete the UOW by persisting to db.  Also publishes any new articles (NB only article persistance is guaranteed as part of the UOW, publication delivery is not guaranteed)
        /// </summary>
        Task CompleteAndPublishAsync();
    }

    public class UnitOfWork : IUnitOfWorkInternal {
        #region Properties and fields
        private readonly IStreamIdHasher streamHasher;
        private readonly IReactorBucketFacade reactorBucketFacade;
        private readonly Func<UnitOfWork, Task<IEnumerable<Messages.ArticlePublished>>> persist;
        public IReactor Reactor { get; private set; }
        public Option<PreviouslyPersistedPubSubData> Previous { get; }
        List<UnpersistedReactorSubscription> UnpersistedReactorSubscriptions { get; set; } = new List<UnpersistedReactorSubscription>();
        List<ReactorPublication> ReactorPublications { get; set; } = new List<ReactorPublication>();
        List<ReactorAggregateSubscription> AggregateSubscriptions { get; set; } = new List<ReactorAggregateSubscription>();
        Option<ReactorPublicationDelivery> Delivery { get; set; } = Option.None();

        #endregion

        #region Private types
        /// <summary>
        /// Represents a reactor subscription which should be persisted as part of the uow
        /// </summary>
        private class UnpersistedReactorSubscription {
            public string Name { get; set; }
            /// <summary>
            /// A hash for the name to speed up db searches
            /// </summary>
            public int NameHash { get => Name.GetHashCode(); }
        }

        /// <summary>
        /// Parameterised SQL Args used when persisting a reactor
        /// </summary>
        private class SQLArgs {
            UnitOfWork uow;

            public SQLArgs(UnitOfWork uow) {
                this.uow = uow;
            }
            public string ReactorBucket { get => uow.Reactor.Bucket; }
            /// <summary>
            /// Used for reactors that have a db identity. leave unset for unpersisted reactors
            /// </summary>
            public long ReactorId { get; set; }
            public string ReactorType { get => uow.Reactor.GetType().AssemblyQualifiedName; }
            public string StateType { get => uow.Reactor.State.GetType().AssemblyQualifiedName; }
            public string StateSerialisation { get => JsonConvert.SerializeObject(uow.Reactor.State); }
            public int ReactorVersionNumber { get => uow.Reactor.VersionNumber + 1; }
        }

        #endregion

        #region Instantiation and interface implementation

        public UnitOfWork(IStreamIdHasher streamHasher, IReactorBucketFacade reactorBucketFacade, IReactor reactor, Option<PreviouslyPersistedPubSubData> previous, Func<UnitOfWork, Task<IEnumerable<Messages.ArticlePublished>>> persist) {
            this.streamHasher = streamHasher;
            this.reactorBucketFacade = reactorBucketFacade;
            Reactor = reactor ;
            Previous = previous;
            this.persist = persist;
        }

        public async Task CompleteAndPublishAsync() {
            IEnumerable<Messages.ArticlePublished> publications = await PersistWithPublicationsAysnc();
            foreach (var articlePublished in publications) {
                reactorBucketFacade.Tell(articlePublished);
            }
        }

        public Task<IEnumerable<Messages.ArticlePublished>> PersistWithPublicationsAysnc() {
            if (Reactor.State == null) throw new ReactorValidationException($"Can't persist Reactor {Reactor.GetType().FullName} if its State property is null");
            return persist(this);
        }

        public void Publish(string name, object article) {
            ReactorPublication.GuardPublicationName(name);

            ReactorPublication publication = 
                // re publish new version by name ..
                Previous.FlatMap(previous =>
                    previous.Publications
                    .Where(pub => pub.Name == name)
                    .HeadOption()
                ).Map(pub => new ReactorPublication { Id = pub.Id, Name = name, Article = article, VersionNumber = pub.VersionNumber + 1 })
                // .. or create new version
                .GetOrElse(() => new ReactorPublication { Id = Option.None(), Name = name, Article = article, VersionNumber = 1 });
            ReactorPublications.Add(publication);
        }
        public void RecordDelivery(ReactorPublicationDelivery delivery) => Delivery = delivery.ToSome();
        public void Subscribe(ReactorAggregateSubscription subscription) => AggregateSubscriptions.Add(subscription);
        public void Subscribe(Guid aggregateId, IEventStream stream) {
            Subscribe(new ReactorAggregateSubscription { AggregateId = aggregateId, StreamRevision = stream.CommittedEvents.Count });
        }
        public void Subscribe(string publicationName) {
            ReactorPublication.GuardPublicationName(publicationName);
            UnpersistedReactorSubscriptions.Add(new UnpersistedReactorSubscription { Name = publicationName });
        }

        public void RecordDelivery(ReactorAggregateSubscription subscription) => AggregateSubscriptions.Add(subscription);

#endregion 

        #region SQL persistance

        /// <summary>
        /// Creates a single T SQL statement wrapped in a transaction to persist all parts of the UOW
        /// </summary>
        /// <returns></returns>
        public (StringBuilder, object) GetSQLAndArgs() {
            //prepare string builder + args
            var sb = new StringBuilder();
            var args = new SQLArgs(this);

            sb.Append(@"
SET XACT_ABORT ON

BEGIN TRAN

--reactor id of the reactor being persisted.  We set this within T-SQL script (it's not parameterised)
DECLARE @PersistingReactorID BIGINT
");
            SerialiseReactorRecord(sb, args);
            SerialiseReactorSubscriptionRecords(sb, args);
            SerialiseAggregateRecords(sb, args);
            SerialiseReactorPublicationRecords(sb, args);
            SerialiseDeliveryRecord(sb, args);

            sb.Append(@"
COMMIT");

            return (sb, args);
        }

        private void SerialiseDeliveryRecord(StringBuilder sb, SQLArgs args) {
            if (!Delivery.HasValue) return;
            ReactorPublicationDelivery delivery = Delivery.Get();

            sb.Append($@"
MERGE [dbo].[ReactorPublicationDeliveries] AS TARGET
USING (SELECT {delivery.SubscriptionId} AS [SubscriptionId], {delivery.PublicationId} AS [PublicationId], {delivery.VersionNumber} AS [VersionNumber]) AS SOURCE
ON TARGET.[SubscriptionId] = SOURCE.[SubscriptionId] AND TARGET.[PublicationId] = SOURCE.[PublicationId]
WHEN MATCHED THEN
    UPDATE SET VersionNumber = SOURCE.VersionNumber, LastDeliveryDate = GETDATE()
WHEN NOT MATCHED THEN 
    INSERT ([SubscriptionId],[PublicationId],[VersionNumber],[LastDeliveryDate]) 
    VALUES (SOURCE.[SubscriptionId],SOURCE.[PublicationId],SOURCE.[VersionNumber],GETDATE());
");
        }

        private void SerialiseReactorRecord(StringBuilder sb, SQLArgs args) {
            if (!Reactor.Id.HasValue) {
                sb.Append(@"
INSERT INTO [dbo].[Reactors]
    ([Bucket]
    ,[FactoryId]
    ,[ReactorType]
    ,[StateType]
    ,[StateSerialisation]
    ,[VersionNumber])
VALUES
    (@ReactorBucket
    ,1
    ,@ReactorType
    ,@StateType
    ,@StateSerialisation
    ,1);
SELECT @PersistingReactorID = SCOPE_IDENTITY();");
            } else {
                args.ReactorId = Reactor.Id.Get();

                //Set declared variable to the parameterised SQL
                sb.Append("SELECT @PersistingReactorID = @ReactorId;");

                //todo optimistic concurrency when updating reactors
                sb.Append(@"
UPDATE [dbo].[Reactors]
SET 
    [StateSerialisation] = @StateSerialisation
    ,[StateType] = @StateType
    ,[VersionNumber] = @ReactorVersionNumber
WHERE Id=@PersistingReactorID;");
            }
        }

        private void SerialiseAggregateRecords(StringBuilder sb, SQLArgs args) {
            foreach(var subscription in AggregateSubscriptions) {
                var streamId = streamHasher.GetHash(subscription.AggregateId.ToString());

                sb.Append($@"
MERGE [dbo].[ReactorAggregateSubscriptions] AS TARGET
USING (SELECT '{streamId}' AS StreamId, @PersistingReactorID AS ReactorId, '{subscription.AggregateId}' AS AggregateId, {subscription.StreamRevision} AS StreamRevision) AS SOURCE
ON TARGET.StreamId = SOURCE.StreamId AND TARGET.ReactorId = SOURCE.ReactorId
WHEN MATCHED THEN
    UPDATE SET StreamRevision = SOURCE.StreamRevision
WHEN NOT MATCHED THEN 
    INSERT (StreamId, ReactorId, AggregateId, StreamRevision) VALUES (SOURCE.StreamId, SOURCE.ReactorId, SOURCE.AggregateId, SOURCE.StreamRevision);
");
            }
        }

        private void SerialiseReactorPublicationRecords(StringBuilder sb, SQLArgs args) {
            sb.Append(@"
--holds all the publications which are persisted in the UOW
DECLARE @NewPublications TABLE (  
    Id BIGINT NOT NULL,  
	[PublishingReactorId] BIGINT NOT NULL,
    [Name] VARCHAR(2048) NOT NULL,
    [NameHash] INT NOT NULL,
	[ArticleSerialisationType] NVARCHAR(MAX) NOT NULL,
    [ArticleSerialisation] NVARCHAR(MAX) NOT NULL,
	[VersionNumber] INT NOT NULL); ");

            //outputs updated and inserted records into the publication table
            const string sqlOutput = "OUTPUT inserted.Id, inserted.[PublishingReactorId], inserted.Name, inserted.NameHash, inserted.ArticleSerialisationType, inserted.ArticleSerialisation, inserted.VersionNumber  INTO @NewPublications";

            // updates first
            foreach (var publication in ReactorPublications.Where(pub => pub.Id.HasValue)) {
                string articleType = publication.Article.GetType().AssemblyQualifiedName;
                string articleSerialisation = JsonConvert.SerializeObject(publication.Article);

                //update first
                sb.Append($@"
UPDATE [dbo].[ReactorPublications]
SET [ArticleSerialisationType] ='{articleType}'
,[ArticleSerialisation] = '{articleSerialisation}'
,[VersionNumber] = {publication.VersionNumber}
,[LastPublishedDate] = GETDATE()
{sqlOutput}
WHERE ID = {publication.Id.Get()};");
            }

            // inserts next

            //guard no inserts
            if (!ReactorPublications.Any(pub => !pub.Id.HasValue)) return;

            sb.Append($@"
INSERT INTO [dbo].[ReactorPublications]
    ([Name]
    ,[PublishingReactorId]
    ,[NameHash]
    ,[ArticleSerialisationType]
    ,[ArticleSerialisation]
    ,[VersionNumber]
    ,[LastPublishedDate])
{sqlOutput}
VALUES");

            List<string> values = new List<string>();
            foreach (var publication in ReactorPublications.Where(pub => !pub.Id.HasValue)) {
                string articleType = publication.Article.GetType().AssemblyQualifiedName;
                string articleSerialisation = JsonConvert.SerializeObject(publication.Article);
                values.Add($@"
   ('{publication.Name}'
   , @PersistingReactorID
   ,{ publication.NameHash}
    ,'{articleType}'
    ,'{articleSerialisation}'
    ,1
    ,GETDATE())");
            }

            sb.Append(string.Join(",", values));
            sb.Append(@";
--get the subscribers to the newly updated or inserted publications
SELECT R.Bucket AS [SubscribingReactorBucket], NP.Name, NP.PublishingReactorId, RS.SubscribingReactorId, NP.VersionNumber, NP.ArticleSerialisation, NP.ArticleSerialisationType, RS.Id AS SubscriptionId, NP.Id AS [PublicationId]
FROM  
	@NewPublications NP

	INNER JOIN dbo.ReactorSubscriptions RS
		ON NP.NameHash = RS.NameHash
		AND NP.Name = RS.Name
    INNER JOIN dbo.Reactors R
        ON RS.SubscribingReactorId  = R.Id
");
        }

        private void SerialiseReactorSubscriptionRecords(StringBuilder sb, SQLArgs args) {
            foreach (var subscription in UnpersistedReactorSubscriptions) {
                sb.Append($@"
INSERT INTO [dbo].[ReactorSubscriptions]
    ([SubscribingReactorId]
    ,[Name]
    ,[NameHash])
VALUES(
    @PersistingReactorID
    ,'{subscription.Name}'
    ,{subscription.NameHash}
);");
            }
        }

        #endregion
    }
}
