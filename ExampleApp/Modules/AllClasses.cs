using Autofac;
using EventSaucing.StreamProcessors;

namespace ExampleApp.Modules
{
    public class AllClasses : Module
    {
        protected override void Load(ContainerBuilder builder) {


            //builder.RegisterType<StreamProcessorTypeProvider>().As<IStreamProcessorTypeProvider>();
        }
    }
}
