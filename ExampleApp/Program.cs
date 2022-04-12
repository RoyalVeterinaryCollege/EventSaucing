using Autofac;
using Autofac.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace ExampleApp; 

public class Program {
    public static int Main(string[] args) {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();


        try {
            Log.Information("Starting web host");
            RunApp(args);
            return 0;
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Host terminated unexpectedly");
            return 1;
        }
        finally {
            Log.CloseAndFlush();
        }
    }

    public static void RunApp(string[] args) {
        var builder = WebApplication
            .CreateBuilder(args);

        //use autofac for DI
        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

        //use serilog for logging
        builder.Host.UseSerilog();

        // Add services to the container.
        builder.Services.AddRazorPages();

        var startup = new Startup(builder.Configuration);

        // Register services directly with Autofac here. Don't
        // call builder.Populate(), that happens in AutofacServiceProviderFactory.
        builder.Host.ConfigureContainer<ContainerBuilder>(b => startup.ConfigureContainer(b));

        startup.ConfigureServices(builder.Services);

        var app = builder.Build();
        startup.Configure(app, app.Environment);
        app.Run();
    }
}