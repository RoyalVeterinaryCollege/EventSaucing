using Autofac;
using CommonDomain;
using CommonDomain.Core;
using CommonDomain.Persistence;
using CommonDomain.Persistence.EventStore;
using EventSaucing.Aggregates;
using EventSaucing.NEventStore;
using NEventStore;
using NEventStore.Persistence.Sql;
using NEventStore.Persistence.Sql.SqlDialects;

namespace EventSaucing.DependencyInjection.Autofac { 

    /// <summary>
    /// Module for setting up NEventStore
    /// </summary>
    public class NEventStoreModule : Module
    {
        protected override void Load(ContainerBuilder builder) {
            builder.RegisterType<LoggerAdapter>()
                   .As<global::NEventStore.Logging.ILog>()
                   .SingleInstance();

            builder.RegisterType<AkkaCommitPipeline>().SingleInstance();
            builder.Register(c => {
                var eventStoreLogger = c.Resolve<global::NEventStore.Logging.ILog>();
                var eventStore = Wireup.Init()
                                       .HookIntoPipelineUsing(c.Resolve<AkkaCommitPipeline>())
                                       .LogTo(type => eventStoreLogger)
                                       .UsingSqlPersistence(c.Resolve<IConnectionFactory>())
                                       .WithDialect(new MsSqlDialect())
                                       .InitializeStorageEngine()
                                       .UsingCustomSerialization(new JsonSerializer())
                                       .Build();
                return eventStore;
            }).SingleInstance();
            
         
            builder.Register(c => c.Resolve<IStoreEvents>().Advanced).SingleInstance();
            builder.RegisterType<SharedEventApplicationRoutes>()
                .As<ISharedEventApplicationRoutes>()
                .SingleInstance();
            builder.RegisterType<AggregateFactory>()
                   .As<IConstructAggregates>()
                   .SingleInstance();
            builder.RegisterType<ConflictDetector>()
                   .As<IDetectConflicts>()
                   .SingleInstance();
            builder.RegisterType<EventStoreRepository>()
                .As<IRepository>()
                .SingleInstance();
            builder.RegisterType<InMemoryCommitSerialiserCache>()
                   .As<IInMemoryCommitSerialiserCache>();

        }
    }
}