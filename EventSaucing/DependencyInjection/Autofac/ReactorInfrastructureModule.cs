using Autofac;
using Dapper;
using Scalesque;

namespace EventSaucing.DependencyInjection.Autofac {

    /// <summary>
    /// Registers Reactor classes.  Don't register yourself, use <see cref="ModuleRegistrationExtensions.RegisterEventSaucingModules"/> 
    /// </summary>
    public class ReactorInfrastructureModule : Module {
        protected override void Load(ContainerBuilder builder) {
            //tell dapper how to handle Option<long> which is used in reactor persistence
            SqlMapper.AddTypeHandler(typeof(Option<long>), new Storage.OptionHandler());

            //todo: add new reactor code
        }
    }
}
