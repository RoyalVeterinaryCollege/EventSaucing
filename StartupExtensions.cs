using Akka.Actor;
using Akka.DI.Core;
using Autofac;
using Autofac.Core.Registration;
using EventSaucing.Akka.Actors;
using EventSaucing.DependencyInjection.Autofac;
using EventSaucing.Projector;
using EventSaucing.Reactors;
using EventSaucing.Storage;
using System;

namespace EventSaucing {
	/// <summary>
	/// Holds extension methods to facilitate configuration and startup of event saucing
	/// </summary>
    public static class StartupExtensions {
		/// <summary>
		/// Registers modules required for EventSaucing.  Starts Akka. You must call this before starting eventsaucing in any way (including starting a local reactor bucket)
		/// </summary>
		public static IModuleRegistrar RegisterEventSaucingModules(this ContainerBuilder builder, EventSaucingConfiguration configuration) {
			if (builder == null)
				throw new ArgumentNullException("builder");
			if (configuration == null)
				throw new ArgumentNullException("configuration");

			builder.RegisterInstance(configuration);
			return builder
				.RegisterModule(new DatabaseConnectivity())
				.RegisterModule(new NEventStoreModule(configuration.UseProjectorPipeline))
				.RegisterModule(new AkkaModule(configuration))
				.RegisterModule(new ReactorInfrastructureModule());
		}

		/// <summary>
		/// Starts the main EventSaucing projector pipeline. Assumes you have already called RegisterEventSaucingModules.
		/// </summary>
		public static IContainer StartEventSaucingProjectorPipeline(this IContainer container) {
			// Ensure the Projector Status is initialised.
			var dbService = container.Resolve<IDbService>();
			ProjectorHelper.InitialiseProjectorStatusStore(dbService);

            var actorSystem = container.Resolve<ActorSystem>();
            var commitSerialisor = actorSystem.ActorOf(actorSystem.DI().Props<CommitSerialiserActor>(), nameof(CommitSerialiserActor));
            container.Resolve<ActorPaths>().LocalCommitSerialisor = commitSerialisor.Path;
			return container;
		}
		/// <summary>
		/// Starts Reactor supervision for an akka cluster.  Need to call this once per akka cluster on the main node.  You call this even if your cluster only has one node. Assumes you have already called RegisterEventSaucingModules.
		/// </summary>
		/// <param name="container"></param>
		/// <param name="localReactorBucketName"></param>
		/// <returns></returns>
		public static IContainer StartEventSaucingReactorClusterSupervision(this IContainer container, string localReactorBucketName) {
			//create the reactor persistence tables if not already created
			var reactorRepo = container.Resolve<IReactorRepository>();
			reactorRepo.CreateReactorTablesAsync().Wait();

			var actorSystem = container.Resolve<ActorSystem>();

			//start the overall reactor infrastrucure, only one of these needed per cluster
			var reactorsuper = actorSystem.ActorOf(actorSystem.DI().Props<ReactorSupervisor>(), name: "reactor-supervisor");

			//tell the local infrastructure its bucket identity
			reactorsuper.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket(localReactorBucketName));
			return container;
		}
		/// <summary>
		/// Starts a local Reactor bucket which automaticaly connects to the main EventSuacing Reactor system.  Use this for secondary Reactor processes outside of the main webserver. Assumes you have already called RegisterEventSaucingModules.
		/// </summary>
		/// <param name="container"></param>
		/// <param name="localReactorBucketName"></param>
		/// <returns></returns>
		public static IContainer StartEventSaucingReactorNode(this IContainer container, string localReactorBucketName) {
			var actorSystem = container.Resolve<ActorSystem>();

			//start the local reactor bucket supervisor.  It will automatically connect to the main Reactor process.
			var bucket = actorSystem.ActorOf(actorSystem.DI().Props<ReactorBucketSupervisor>(), name: "reactor-bucket");
			bucket.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket(localReactorBucketName));
			return container;
		}
	}
}