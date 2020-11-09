using Autofac;
using Dapper;
using EventSaucing.Reactors;
using Scalesque;

namespace EventSaucing.DependencyInjection.Autofac {
    public class ReactorModule : Module {
        protected override void Load(ContainerBuilder builder) {
            //tell dapper how to handle Option<long> which is used frequently in reactor persistance
            SqlMapper.AddTypeHandler(typeof(Option<long>), new Storage.OptionHandler());

            builder.RegisterType<ReactorBucketRouter>().As<IReactorBucketRouter>().SingleInstance();
            builder.RegisterType<ReactorRepository>().As<IReactorRepository>().SingleInstance();
            builder.RegisterType<ReactorPublicationFinder>().As<IReactorPublicationFinder>().SingleInstance();
            builder.RegisterType<ReactorActor>();
            builder.RegisterType<ReactorStartup>().As<IStartable>().SingleInstance();
        }
    }
}
