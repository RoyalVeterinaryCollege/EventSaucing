using Akka.Actor;
using Akka.DI.Core;
using Autofac;
using EventSaucing.Akka.Actors;
using EventSaucing.Projector;
using EventSaucing.Reactors;
using EventSaucing.Storage;

namespace EventSaucing.DependencyInjection.Autofac {
    public static class ContainerExtensions {

		/// <summary>
		/// Starts the main EventSaucing pipeline
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
		/// Starts the main EventSaucing pipeline and also starts the Reactors main supervisor including RoyalMail
		/// </summary>
		/// <param name="container"></param>
		/// <param name="localReactorBucketName"></param>
		/// <returns></returns>
		public static IContainer StartEventSaucingWithReactors(this IContainer container, string localReactorBucketName) {
			StartEventSaucing(container);

			var actorSystem = container.Resolve<ActorSystem>();
			var propsResolver = container.Resolve<IDependencyResolver>();

			//start the overall reactor infrastrucure, only one of these needed per cluster
			var reactorsuper = actorSystem.ActorOf(propsResolver.Create<ReactorSupervisor>(), name: "reactor-supervisor");

			//tell the local infrastructure its bucket identity
			reactorsuper.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket(localReactorBucketName));
			return container;
		}
		/// <summary>
		/// Starts a local Reactor bucket which automatically connects to the main EventSuacing Reactor system.  Use this for secondary Reactor processes outside of the main webserver
		/// </summary>
		/// <param name="container"></param>
		/// <param name="localReactorBucketName"></param>
		/// <returns></returns>
		public static IContainer StartLocalReactorBucket(this IContainer container, string localReactorBucketName) {
			var actorSystem = container.Resolve<ActorSystem>();
			var propsResolver = container.Resolve<IDependencyResolver>();
			actorSystem.AddDependencyResolver(propsResolver);

			//start the local reactor bucket supervisor.  It will automatically connect to the main process.
			var bucket = actorSystem.ActorOf(propsResolver.Create<ReactorBucketSupervisor>(), name: "reactor-bucket");
			bucket.Tell(new ReactorBucketSupervisor.LocalMessages.SubscribeToBucket(localReactorBucketName));
			return container;
		}
	}
}