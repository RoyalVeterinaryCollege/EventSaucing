using System;
using Autofac;
using Autofac.Core.Registration;
using Microsoft.Extensions.Logging;

namespace EventSaucing.DependencyInjection.Autofac {
	public static class ModuleRegistrationExtensions {
        /// <summary>
        /// Registers modules required for EventSaucing.  You must have already registered an <see cref="ILogger"/> implementation before calling this.
        /// </summary>
        /// <param name="builder">The builder to register the modules with.</param>
        /// <param name="configuration">EventSaucingConfiguration</param>
        /// <exception cref="T:System.ArgumentNullException">Thrown if <paramref name="builder"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <returns>
        /// The <see cref="T:Autofac.Core.Registration.IModuleRegistrar"/> to allow
        ///             additional chained module registrations.
        /// </returns>
        public static IModuleRegistrar RegisterEventSaucingModules(this ContainerBuilder builder, EventSaucingConfiguration configuration) {
			if (builder == null)
				throw new ArgumentNullException(nameof(builder));
			if (configuration == null)
				throw new ArgumentNullException(nameof(configuration));

			builder.RegisterInstance(configuration);
            return builder
                .RegisterModule(new DatabaseConnectivityModule())
                .RegisterModule(new NEventStoreModule())
                .RegisterModule(new ReactorInfrastructureModule());
        }
	}
}
