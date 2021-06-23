using Akka.Actor;
using Akka.DI.Core;
using Autofac;

namespace EventSaucing.DependencyInjection.Autofac {
    /// <summary>
    /// As soon as the container and akka are ready, this configures akka to use autofac as for IoC
    /// </summary>
    public class AkkaAutofacConfigurer : IStartable {
        private readonly IDependencyResolver resolver;
        private readonly ActorSystem system;

        public AkkaAutofacConfigurer(IDependencyResolver resolver, ActorSystem system) {
            this.resolver = resolver;
            this.system = system;
        }
        public void Start() {
            system.AddDependencyResolver(resolver); //configure akka to use resolver
        }
    }
}
