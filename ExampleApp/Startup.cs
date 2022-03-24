using Akka.Actor;
using Akka.DependencyInjection;
using Autofac;
using EventSaucing;
using EventSaucing.HostedServices;
using ExampleApp.Modules;

namespace ExampleApp {
    public class Startup {
        public Startup(IConfiguration configuration) {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }


        public void ConfigureContainer(ContainerBuilder builder) {
            //These values should be specified in a file called .env which should be kept out of source control
            var userid = Environment.GetEnvironmentVariable("UserId") ?? "UserId_environment_variable_missing";
            var password = Environment.GetEnvironmentVariable("Password") ?? "missing";


            EventSaucingConfiguration eventsaucingconfiguration = new EventSaucingConfiguration  {
                ActorSystemName = "ExampleApp",
                CommitStoreConnectionString = string.Format(Configuration.GetConnectionString("CommitStore"), userid, password),
                ReadmodelConnectionString = string.Format(Configuration.GetConnectionString("Readmodel"), userid, password)
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