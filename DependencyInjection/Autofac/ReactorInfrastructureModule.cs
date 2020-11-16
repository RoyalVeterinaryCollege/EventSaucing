﻿using Autofac;
using Dapper;
using EventSaucing.Reactors;
using Scalesque;

namespace EventSaucing.DependencyInjection.Autofac {

    /// <summary>
    /// A self contained module for using EventSaucing reactors
    /// </summary>
    public class ReactorInfrastructureModule : Module {
        private readonly EventSaucingConfiguration configuration;

        public ReactorInfrastructureModule(EventSaucingConfiguration configuration) {
            this.configuration = configuration;
        }
        protected override void Load(ContainerBuilder builder) {
            builder.RegisterInstance(configuration);

            //tell dapper how to handle Option<long> which is used frequently in reactor persistance
            SqlMapper.AddTypeHandler(typeof(Option<long>), new Storage.OptionHandler());

            //reactor services
            builder.RegisterType<ReactorBucketFacade>().As<IReactorBucketFacade>().SingleInstance();
            builder.RegisterType<ReactorRepository>().As<IReactorRepository>().SingleInstance();
            builder.RegisterType<ReactorPublicationFinder>().As<IReactorPublicationFinder>().SingleInstance();

            //reactor actors
            builder.RegisterType<ReactorSupervisor>();
            builder.RegisterType<ReactorActor>();
            builder.RegisterType<ReactorBucketSupervisor>();
            builder.RegisterType<RoyalMail>();
        }
    }
}
