using Akka.Actor;
using Akka.DependencyInjection;
using Autofac;
using EventSaucing;
using EventSaucing.HostedServices;
using ExampleApp.Modules;
using System.Configuration;
using Akka.Configuration;
using ExampleApp.Services;
using ConfigurationManager = System.Configuration.ConfigurationManager;

namespace ExampleApp {
    public class Startup {
        public Startup(IConfiguration configuration) {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureContainer(ContainerBuilder builder) {
            //get akka settings from config, and replace placeholders
            var akkaConfig = new {
                akka_journal_db = Configuration.GetConnectionString("AkkaJournal"),
                akka_snapshot_db = Configuration.GetConnectionString("AkkaSnapshotStore"),
                cluster_ip  = Configuration.GetValue<string>("Akka:ClusterIP"),
                cluster_port = Configuration.GetValue<string>("Akka:ClusterPort"),
                // split this on , and insert quotes around each
                cluster_seeds = Configuration.GetValue<string>("Akka:ClusterSeeds")
                    .Split(",")
                    .Select(x => $"\"{x}\"")
            };

            string akkaHconf = File.ReadAllText("akka_hconf.txt")
                .Replace("{AkkaJournal}", akkaConfig.akka_journal_db)
                .Replace("{AkkaSnapshotStore}", akkaConfig.akka_snapshot_db)
                .Replace("{AkkaClusterIP}", akkaConfig.cluster_ip)
                .Replace("{AkkaClusterPort}", akkaConfig.cluster_port)
                .Replace("{AkkaClusterSeeds}", "["  + string.Join(",",akkaConfig.cluster_seeds) + "]");


            EventSaucingConfiguration eventsaucingconfiguration = new EventSaucingConfiguration  {
                ActorSystemName = "ExampleApp",
                ClusterConnectionString = Configuration.GetConnectionString("CommitStore"),
                CommitStoreConnectionString = Configuration.GetConnectionString("CommitStore"),
                ReplicaConnectionString = Configuration.GetConnectionString("Replica"),
                AkkaConfig = ConfigurationFactory.ParseString(akkaHconf)
            };
             
            // register EventSaucingModules in ConfigureContainer
            builder.RegisterEventSaucingModules(eventsaucingconfiguration);

            // Register your own things directly with Autofac here. Don't
            // call builder.Populate(), that happens in AutofacServiceProviderFactory
            // for you.
            builder.RegisterModule<LoggingModule>(); // you must implement a logging module like this, EventSaucing wont register a logger for you, but expects one to be available
            builder.RegisterModule<ServicesModule>();

        }


        public void ConfigureServices(IServiceCollection services) {
            services.AddRazorPages();
            services.AddSingleton<IConfiguration>(Configuration);

            // add EventSaucing services.  Then add the HostedServices in any order
            services.AddEventSaucing();
            services.AddHostedService<StreamProcessorService>(); // optional stream processor service.  This starts and hosts your stream processors
            services.AddHostedService<UserActivitySimulatorService>(); // host which simulates user activity in the front end
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