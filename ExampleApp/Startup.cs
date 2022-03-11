﻿using Akka.Actor;
using Akka.DependencyInjection;
using Autofac;
using EventSaucing;
using EventSaucing.HostedServices;

namespace ExampleApp {
    public class Startup {
        public Startup(IConfiguration configuration) {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }


        public void ConfigureContainer(ContainerBuilder builder)
        {
            EventSaucingConfiguration eventsaucingconfiguration = new EventSaucingConfiguration
            {
                ConnectionString = Configuration.GetConnectionString("SqlConnectionString"),
                ActorSystemName = "CRIS3",
                // todo move akka config to hconf file
            };

            builder.RegisterEventSaucingModules(eventsaucingconfiguration);

            // Register your own things directly with Autofac here. Don't
            // call builder.Populate(), that happens in AutofacServiceProviderFactory
            // for you.
            /*
            builder.RegisterModule(new LoggingModule());
            builder.RegisterModule(new DatabaseModule(_config));
            builder.RegisterModule(new DatabaseBuilderModule(_config, _env));
            builder.RegisterModule(new MVCPipelineModule(_config));
            EventSaucingConfiguration eventsaucingconfiguration = new EventSaucingConfiguration
            {
                ConnectionString = _config.GetConnectionString("SqlConnectionString"),
                ActorSystemName = "CRIS3",
                UseProjectorPipeline = true
                // akka config is now stored in app.config
            };
            builder.RegisterEventSaucingModules(eventsaucingconfiguration);
            builder.RegisterModule(new AuditModule());
            builder.RegisterModule(new DomainServicesModule(_config));
            builder.RegisterModule(new MigrationModule(_config));
            builder.RegisterModule(new ReadmodelSubscriptionModule(_config));
            builder.RegisterModule(new CrisReactorsModule());*/
        }


        public void ConfigureServices(IServiceCollection services) {
            services.AddRazorPages();

            services.AddSingleton<IConfiguration>(Configuration);
            services.AddSingleton(sp => {
                var bootstrap = BootstrapSetup.Create();
                var di = DependencyResolverSetup.Create(sp);
                var actorSystemSetup = bootstrap.And(di);
                var actorSystem = ActorSystem.Create("Test", actorSystemSetup); //todo actorsystem name from config
                return actorSystem;
            });

            // add hosted services.
            // CoreServices needs to come first, then any order
            services.AddHostedService<CoreServices>(); // required
            //services.AddHostedService<ProjectorServices>(); // optional
            //services.AddHostedService<ReactorServices>(); // optional
        }

      

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
            if (!env.IsDevelopment()) {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints => { endpoints.MapRazorPages(); });
        }
    }
}