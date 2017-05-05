using System;
using System.Runtime.Remoting.Messaging;
using Autofac;
using Autofac.Core.Registration;

namespace EventSaucing.DependencyInjection.Autofac {
	public static class ModuleRegistrationExtensions {
		/// <summary>
		/// Registers modules required for EventSaucing.
		/// 
		/// </summary>
		/// <param name="builder">The builder to register the modules with.</param>
		/// <param name="connectionString">The connection string to the db store.</param>
		/// <exception cref="T:System.ArgumentNullException">Thrown if <paramref name="builder"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
		/// <returns>
		/// The <see cref="T:Autofac.Core.Registration.IModuleRegistrar"/> to allow
		///             additional chained module registrations.
		/// 
		/// </returns>
		public static IModuleRegistrar RegisterEventSaucingModules(this ContainerBuilder builder, EventSaucingConfiguration configuration) {
			if (builder == null)
				throw new ArgumentNullException("builder");
			if (configuration == null)
				throw new ArgumentNullException("configuration");

			builder.RegisterInstance(configuration);
			return builder
				.RegisterModule(new DatabaseConnectivity())
				.RegisterModule(new NEventStoreModule())
				.RegisterModule(new AkkaModule());
		}
	}
}
