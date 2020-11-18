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
				.RegisterModule(new ReactorInfrastructureModule(configuration));
		}

		/// <summary>
		/// Starts the main EventSaucing projector pipeline without Reactor support. Assumes you have already called RegisterEventSaucingModules.
		/// </summary>
		public static IContainer StartEventSaucing(this IContainer container) {
			// Ensure the Projector Status is initialised.
			var dbService = container.Resolve<IDbService>();
			ProjectorHelper.InitialiseProjectorStatusStore(dbService);

            var actorSystem = container.Resolve<ActorSystem>();
            var propsResolver = container.Resolve<IDependencyResolver>();
            actorSystem.AddDependencyResolver(propsResolver);
            var commitSerialisor = actorSystem.ActorOf(propsResolver.Create<CommitSerialiserActor>(), nameof(CommitSerialiserActor));
            container.Resolve<ActorPaths>().LocalCommitSerialisor = commitSerialisor.Path;
			return container;
		}
		/// <summary>
		/// Starts the main EventSaucing pipeline and also starts the Reactors main supervisor including RoyalMail.  Assumes you have already called RegisterEventSaucingModules.
		/// </summary>
		/// <param name="container"></param>
		/// <param name="localReactorBucketName"></param>
		/// <returns></returns>
		public static IContainer StartEventSaucingWithReactors(this IContainer container, string localReactorBucketName) {
			StartEventSaucing(container);
			//todo : Create Reactor database tables

			var actorSystem = container.Resolve<ActorSystem>();
			var propsResolver = container.Resolve<IDependencyResolver>();

			//start the overall reactor infrastrucure, only one of these needed per cluster
			var reactorsuper = actorSystem.ActorOf(propsResolver.Create<ReactorSupervisor>(), name: "reactor-supervisor");

			//tell the local infrastructure its bucket identity
			reactorsuper.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket(localReactorBucketName));
			return container;
		}
		/// <summary>
		/// Starts a local Reactor bucket which automatically connects to the main EventSuacing Reactor system.  Use this for secondary Reactor processes outside of the main webserver. . Assumes you have already called RegisterEventSaucingModules.
		/// </summary>
		/// <param name="container"></param>
		/// <param name="localReactorBucketName"></param>
		/// <returns></returns>
		public static IContainer StartLocalReactorBucket(this IContainer container, string localReactorBucketName) {
			var actorSystem = container.Resolve<ActorSystem>();
			var propsResolver = container.Resolve<IDependencyResolver>();
			actorSystem.AddDependencyResolver(propsResolver); //combine Akka with Autofac if this hasn't been done yet

			//start the local reactor bucket supervisor.  It will automatically connect to the main Reactor process.
			var bucket = actorSystem.ActorOf(propsResolver.Create<ReactorBucketSupervisor>(), name: "reactor-bucket");
			bucket.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket(localReactorBucketName));
			return container;
		}
	}
}