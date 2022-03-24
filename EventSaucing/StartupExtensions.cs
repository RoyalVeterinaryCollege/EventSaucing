using Autofac;
using Autofac.Core.Registration;
using EventSaucing.DependencyInjection.Autofac;
using System;
using Akka.Actor;
using Akka.DependencyInjection;
using EventSaucing.HostedServices;
using Microsoft.Extensions.DependencyInjection;

namespace EventSaucing {
	/// <summary>
	/// Holds extension methods to facilitate configuration and startup of event saucing
	/// </summary>
    public static class StartupExtensions {

		/// <summary>
		/// Adds EventSaucing to the MVC services.  This starts Akka.
		/// </summary>
		/// <param name="services">IServiceCollection services</param>
		/// <returns></returns>
		public static IServiceCollection AddEventSaucing(this IServiceCollection services) {
            // start Akka
            // from https://getakka.net/articles/actors/dependency-injection.html

			services.AddSingleton(sp => {
                var config = sp.GetService<EventSaucingConfiguration>();
                var bootstrap = BootstrapSetup.Create().WithConfig(config.AkkaConfig);
                var di = DependencyResolverSetup.Create(sp);
                var actorSystemSetup = bootstrap.And(di);
				var actorSystem = ActorSystem.Create(config.ActorSystemName, actorSystemSetup);

				return actorSystem;
            });
            services.AddHostedService<AkkaShutDownService>(); // performs graceful shutdown
            services.AddHostedService<EventStreamService>();  // required by all other hosted service (at time of writing)

			return services;
        }

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
				.RegisterModule(new DatabaseConnectivityModule())
				.RegisterModule(new NEventStoreModule())
				.RegisterModule(new ReactorInfrastructureModule());
		}
	}
}