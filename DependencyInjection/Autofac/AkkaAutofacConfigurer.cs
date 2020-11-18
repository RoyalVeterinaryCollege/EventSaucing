using Akka.Actor;
using Autofac;

namespace EventSaucing.DependencyInjection.Autofac {
    /// <summary>
    /// As soon as the container and akka are ready, this configures akka to use autofac as for IoC
    /// </summary>
    public class AkkaAutofacConfigurer : IStartable {
        private readonly IContainer container;
        private readonly ActorSystem system;

        public AkkaAutofacConfigurer(IContainer container, ActorSystem system) {
            this.container = container;
            this.system = system;
        }
        public void Start() {
            system.UseAutofac(container);
        }
    }
}
