using Autofac;
using EventSaucing.Storage;
using EventSaucing.Storage.Sql;
using NEventStore.Persistence.Sql;

namespace EventSaucing.DependencyInjection.Autofac {
	public class DatabaseConnectivity : Module {
		protected override void Load(ContainerBuilder builder) {
            builder.RegisterType<SqlDbService>()
                .As<IDbService>()
                .As<IConnectionFactory>()
                .SingleInstance();
        }
    }
}
