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
            string akkaHconf = File.ReadAllText("akka_hconf.txt");

            //These values should be specified in a file called .env which should be kept out of source control
            var userid = Environment.GetEnvironmentVariable("UserId") ?? "UserId_environment_variable_missing";
            var password = Environment.GetEnvironmentVariable("Password") ?? "missing";

            var data = new {
                user_id= Environment.GetEnvironmentVariable("user_id") ?? "UserId_environment_variable_missing",
                password = Environment.GetEnvironmentVariable("password") ?? "missing",
                commitstore_db= Environment.GetEnvironmentVariable("CommitStore_dev"),
                akka_persistence_db = Environment.GetEnvironmentVariable("akka_persistence_db"),
                akka_persistence_table = Environment.GetEnvironmentVariable("akka_persistence_table"),
                akka_snapshot_table = Environment.GetEnvironmentVariable("akka_snapshot_table"),
                readmodel_db = Environment.GetEnvironmentVariable("readmodel_db"),

            };


            EventSaucingConfiguration eventsaucingconfiguration = new EventSaucingConfiguration  {
                ActorSystemName = "ExampleApp",
                //CommitStoreConnectionString = Configuration.GetConnectionString("CommitStore"), userid, password),
                //ReplicaConnectionString = string.Format(Configuration.GetConnectionString("Readmodel"), userid, password),
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
            //services.AddHostedService<ReactorService>(); // optional
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