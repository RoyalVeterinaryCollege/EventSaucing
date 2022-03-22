using Akka.Actor;
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

            // register EventSaucingModules in ConfigureContainer
            builder.RegisterEventSaucingModules(eventsaucingconfiguration);

            // Register your own things directly with Autofac here. Don't
            // call builder.Populate(), that happens in AutofacServiceProviderFactory
            // for you.
        }


        public void ConfigureServices(IServiceCollection services) {
            services.AddRazorPages();
            services.AddSingleton<IConfiguration>(Configuration);

            // add EventSaucing services.  Then add the HostedServices in any order
            services.AddEventSaucing();
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