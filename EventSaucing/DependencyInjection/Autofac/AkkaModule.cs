using System.Reflection;
using Akka.Actor;
using Akka.Configuration;
using Akka.DI.AutoFac;
using Akka.DI.Core;
using Autofac;
using EventSaucing.Projectors;
using Module = Autofac.Module;

namespace EventSaucing.DependencyInjection.Autofac {


    public class AkkaModule : Module {
        private readonly string actorsystemname;

        public AkkaModule(EventSaucingConfiguration config) {
            this.actorsystemname = config.ActorSystemName;
        }
    
        protected override void Load(ContainerBuilder builder) {
            //should come first so akka is configured to use autofac as early as possible during start up, else various components will fail as they can't find their dependencies
            builder.RegisterType<AutoFacDependencyResolver>().As<IDependencyResolver>().SingleInstance();
            builder.RegisterType<AkkaAutofacConfigurer>().As<IStartable>();
            builder.RegisterType<Akka.AkkaStartStop>().As<IStartable>();

			//todo DI makes assumptions about which assemblies contain projectors + other actors 
            //todo the way projectorsupervisor gets refereences to projector types has changed
			builder.RegisterAssemblyTypes(Assembly.GetEntryAssembly()).AssignableTo<LegacyProjector>(); // Get the assembly that kicks the show off, this should have projectors in it.
            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly()).AssignableTo<ReceiveActor>(); // This assembly, which has infrastructure actors.

            builder
                .Register(x => ActorSystem.Create(actorsystemname)) // Akka configured in app.config
                .SingleInstance(); // Akka starts at this point, but without the dependency resolver
        }
    }
}
