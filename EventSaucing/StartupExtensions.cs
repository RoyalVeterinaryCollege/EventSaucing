using Autofac;
using Autofac.Core.Registration;
using EventSaucing.DependencyInjection.Autofac;
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
				.RegisterModule(new ReactorInfrastructureModule());
		}
	}
}