using System;
using Akka.Actor;
using Akka.DependencyInjection;
using Autofac;
using EventSaucing.HostedServices;
using EventSaucing.Storage;
using EventSaucing.Storage.Sql;
using NEventStore.Persistence.Sql;

namespace EventSaucing.DependencyInjection.Autofac {

    /// <summary>
    /// Registers EventSaucing connectivity services.  Don't register yourself, use <see cref="ModuleRegistrationExtensions.RegisterEventSaucingModules"/> 
    /// </summary>
	public class AkkaModule : Module {

        public AkkaModule() {
        }
		protected override void Load(ContainerBuilder builder) {

            //builder.Register()
            // start Akka
            // from https://getakka.net/articles/actors/dependency-injection.html

            // todo load HCONFIG from file
            // var hocon = ConfigurationFactory.ParseString(File.ReadAllText("app.conf"));
            var bootstrap = BootstrapSetup.Create();
            //var di = DependencyResolverSetup.Create(builder.Resolve);
            //var actorSystemSetup = bootstrap.And(di);
            //_actorSystem = ActorSystem.Create(_config.ActorSystemName, actorSystemSetup);
        }
    }


}
