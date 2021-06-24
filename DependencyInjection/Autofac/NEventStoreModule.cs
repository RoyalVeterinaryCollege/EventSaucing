using Autofac;
using EventSaucing.Aggregates;
using EventSaucing.NEventStore;
using Microsoft.Extensions.Logging;
using NEventStore;
using NEventStore.Domain;
using NEventStore.Domain.Core;
using NEventStore.Domain.Persistence;
using NEventStore.Persistence.Sql;
using NEventStore.Persistence.Sql.SqlDialects;

namespace EventSaucing.DependencyInjection.Autofac { 

    /// <summary>
    /// Module for setting up NEventStore
    /// </summary>
    public class NEventStoreModule : Module
    {
        private readonly bool useCommitPipeline;
        /// <summary>
        /// Instantiates NEvenstore Module which registeres the NEventStore types with AutoFac
        /// </summary>
        /// <param name="useCommitPipeline">bool True if you want to use EventSaucing projector pipeline</param>
        public NEventStoreModule(bool useCommitPipeline)
        {
            this.useCommitPipeline = useCommitPipeline;
        }
        protected override void Load(ContainerBuilder builder) {
            builder.RegisterType<AkkaCommitPipeline>().SingleInstance();
            builder.Register(c => {
                var eventStoreLogger = c.Resolve<ILogger>();

                Wireup wireup = useCommitPipeline ?
                    Wireup.Init().HookIntoPipelineUsing(c.Resolve<AkkaCommitPipeline>(), c.ResolveOptional<ICustomPipelineHook>())
                    : Wireup.Init();

                var eventStore = wireup
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
                .InstancePerDependency();
            builder.RegisterType<InMemoryCommitSerialiserCache>()
                   .As<IInMemoryCommitSerialiserCache>();

        }
    }
}