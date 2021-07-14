using System.Reflection;
using Akka.Actor;
using Akka.Configuration;
using Akka.DI.AutoFac;
using Akka.DI.Core;
using Autofac;
using EventSaucing.Projector;
using Module = Autofac.Module;

namespace EventSaucing.DependencyInjection.Autofac {
    /// <summary>
    /// Global registery of important actor paths
    /// </summary>
    public class ActorPaths {
        /// <summary>
        /// Path to the actor which serialises neventstore commits
        /// </summary>
        public ActorPath LocalCommitSerialisor { get; set; }
    }


    public class AkkaModule : Module {
        private readonly string actorsystemname;
        private readonly Config config;

        public AkkaModule(EventSaucingConfiguration config) {
            this.actorsystemname = config.ActorSystemName;
            this.config = config.AkkaConfiguration;
        }
    
        protected override void Load(ContainerBuilder builder) {
            //should come first so akka is configured to use autofac as early as possible during start up, else various components will fail as they can't find their dependencies
            builder.RegisterType<AutoFacDependencyResolver>().As<IDependencyResolver>().SingleInstance();
            builder.RegisterType<AkkaAutofacConfigurer>().As<IStartable>();
            builder.RegisterType<Akka.AkkaStartStop>().As<IStartable>();

			
			builder.RegisterAssemblyTypes(Assembly.GetEntryAssembly()).AssignableTo<ProjectorBase>(); // Get the assembly that kicks the show off, this should have projectors in it.
            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly()).AssignableTo<ReceiveActor>(); // This assembly, which has infrastructure actors.
            builder.Register(x => new ActorPaths()).SingleInstance();

            builder
                .Register(x => ActorSystem.Create(actorsystemname, config))
                .SingleInstance(); // Akka starts at this point, but without the dependency resolver
        }
    }
}
