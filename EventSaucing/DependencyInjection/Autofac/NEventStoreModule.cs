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
    /// Registers NEvent classes.  Don't register yourself, use <see cref="ModuleRegistrationExtensions.RegisterEventSaucingModules"/> 
    /// </summary>
    public class NEventStoreModule : Module
    {
        //todo: need to register logging modules?
        /*
         * using Autofac;
using Microsoft.Extensions.Logging;
using Serilog;

namespace CRIS.API.DI {
    public class LoggingModule : Module {
        protected override void Load(ContainerBuilder builder) {
            builder.Register(c => Log.Logger).SingleInstance();

            // Required by EventSaucing.  NEventstore uses the MS ILogger interface
            var loggerFactory = (ILoggerFactory)new LoggerFactory();
            loggerFactory.AddSerilog(Log.Logger);
            var logger = loggerFactory.CreateLogger("default_logger");
            builder.Register(c => logger).SingleInstance();
        }
    }
}
         */


        protected override void Load(ContainerBuilder builder) {
            builder.RegisterType<PostCommitNotifierPipeline>().SingleInstance();
            builder.Register(c => {
                //var eventStoreLogger = c.Resolve<ILogger>();

                Wireup wireup = 
                    Wireup
                    .Init()
                    .HookIntoPipelineUsing(c.Resolve<PostCommitNotifierPipeline>(),c.ResolveOptional<CustomPipelineHook>());
                    
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
            builder.RegisterType<InMemoryCommitSerialiserCache>()
                   .As<IInMemoryCommitSerialiserCache>();

        }
    }
}