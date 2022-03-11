using Autofac;
using Dapper;
using EventSaucing.Reactors;
using Scalesque;

namespace EventSaucing.DependencyInjection.Autofac {

    /// <summary>
    /// Registers Reactor classes.  Don't register yourself, use <see cref="ModuleRegistrationExtensions.RegisterEventSaucingModules"/> 
    /// </summary>
    public class ReactorInfrastructureModule : Module {
        protected override void Load(ContainerBuilder builder) {
            //tell dapper how to handle Option<long> which is used in reactor persistence
            SqlMapper.AddTypeHandler(typeof(Option<long>), new Storage.OptionHandler());

            //reactor services
            builder.RegisterType<ReactorBucketFacade>().As<IReactorBucketFacade>().SingleInstance();
            builder.RegisterType<ReactorRepository>().As<IReactorRepository>().SingleInstance();
            builder.RegisterType<ReactorPublicationFinder>().As<IReactorPublicationFinder>().SingleInstance();

            //reactor actors
            builder.RegisterType<ReactorActor>();
            builder.RegisterType<ReactorBucketSupervisor>();
            builder.RegisterType<RoyalMail>();
        }
    }
}
