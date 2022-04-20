using Autofac;
using EventSaucing.Aggregates;
using EventSaucing.EventStream;
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
    /// Registers NEvent classes.  Don't register yourself, use <see cref="StartupExtensions.RegisterEventSaucingModules"/> 
    /// </summary>
    public class NEventStoreModule : Module {
        protected override void Load(ContainerBuilder builder) {
            builder.RegisterType<PostCommitNotifierPipeline>().AsSelf().SingleInstance();


            builder.Register(c => {
                var optionalHook = c.ResolveOptional<CustomPipelineHook>();
                Wireup wireup;
                if (optionalHook is null) {
                    wireup = Wireup
                            .Init()
                            .HookIntoPipelineUsing(c.Resolve<PostCommitNotifierPipeline>());
                } else {
                    wireup = Wireup
                            .Init()
                            .HookIntoPipelineUsing(c.Resolve<PostCommitNotifierPipeline>(), optionalHook);
                }

                var eventStore = wireup
                    .WithLoggerFactory(c.Resolve<ILoggerFactory>())
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
        }
    }
}