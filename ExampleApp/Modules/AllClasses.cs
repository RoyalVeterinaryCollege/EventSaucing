using Autofac;
using EventSaucing.Projectors;
using ExampleApp.Services;

namespace ExampleApp.Modules
{
    public class AllClasses : Module
    {
        protected override void Load(ContainerBuilder builder) {


            builder.RegisterType<ProjectorTypeProvider>().As<IProjectorTypeProvider>();
        }
    }
}
