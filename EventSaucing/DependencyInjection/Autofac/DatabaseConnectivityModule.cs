using Autofac;
using EventSaucing.Storage;
using EventSaucing.Storage.Sql;
using NEventStore.Persistence.Sql;

namespace EventSaucing.DependencyInjection.Autofac {

    /// <summary>
    /// Registers EventSaucing connectivity services.  Don't register yourself, use <see cref="ModuleRegistrationExtensions.RegisterEventSaucingModules"/> 
    /// </summary>
	public class DatabaseConnectivityModule : Module {
		protected override void Load(ContainerBuilder builder) {
            builder.RegisterType<SqlDbService>()
                .As<IDbService>()
                .As<IConnectionFactory>()
                .SingleInstance();
        }
    }
}
