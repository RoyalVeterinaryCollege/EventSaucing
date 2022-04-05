using Autofac;
using Dapper;
using EventSaucing.EventStream;
using Scalesque;

namespace EventSaucing.DependencyInjection.Autofac {

    /// <summary>
    /// Registers EventSaucing services classes.  Don't register yourself, use <see cref="StartupExtensions.RegisterEventSaucingModules"/> 
    /// </summary>
    public class ServicesModule : Module {
        protected override void Load(ContainerBuilder builder) {
            //tell dapper how to handle Option<long> 
            SqlMapper.AddTypeHandler(typeof(Option<long>), new Storage.OptionHandler());
            builder.RegisterType<InMemoryCommitSerialiserCache>()
                .As<IInMemoryCommitSerialiserCache>();
        }
    }
}
