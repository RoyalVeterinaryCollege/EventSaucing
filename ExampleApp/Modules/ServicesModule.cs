using Autofac;
using EventSaucing.StreamProcessors;
using ExampleApp.Services;

namespace ExampleApp.Modules
{
    public class ServicesModule : Module
    {
        protected override void Load(ContainerBuilder builder) {
            builder.RegisterType<StreamProcessorPropsProvider>().As<IStreamProcessorInitialisation>();
        }
    }
}
