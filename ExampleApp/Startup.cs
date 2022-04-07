using Akka.Actor;
using Akka.DependencyInjection;
using Autofac;
using EventSaucing;
using EventSaucing.HostedServices;
using ExampleApp.Modules;
using System.Configuration;
using Akka.Configuration;
using ConfigurationManager = System.Configuration.ConfigurationManager;

namespace ExampleApp {
    public class Startup {
        public Startup(IConfiguration configuration) {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }


        public void ConfigureContainer(ContainerBuilder builder) {
            //get akka connection strings from config, and replace placeholders
            var akkaConfig = new {
                akka_journal_db = Configuration.GetConnectionString("AkkaJournal"),
                akka_snapshot_db = Configuration.GetConnectionString("AkkaSnapshotStore"),
            };
            string akkaHconf = File.ReadAllText("akka_hconf.txt")
                .Replace("{akka_journal_db}", akkaConfig.akka_journal_db)
                .Replace("{akka_snapshot_db}", akkaConfig.akka_snapshot_db);




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
            builder.RegisterModule<AllClasses>();
        }


        public void ConfigureServices(IServiceCollection services) {
            services.AddRazorPages();
            services.AddSingleton<IConfiguration>(Configuration);

            // add EventSaucing services.  Then add the HostedServices in any order
            // make sure you have provided Akka config in app.config
            services.AddEventSaucing();
            //services.AddHostedService<ProjectorService>(); // optional
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