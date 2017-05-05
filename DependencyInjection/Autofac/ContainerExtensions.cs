using Akka.Actor;
using Akka.DI.Core;
using Autofac;
using EventSaucing.Akka.Actors;
using EventSaucing.Projector;
using EventSaucing.Storage;

namespace EventSaucing.DependencyInjection.Autofac {
    public static class ContainerExtensions {

		/// <summary>
		/// This class is responsible for starting up any actors which should be running at applictation start up
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
    }
}