using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using DbUp;
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
            return RunApp(args);
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Host terminated unexpectedly");
            return 1;
        }
        finally {
            Log.CloseAndFlush();
        }
    }

    public static int RunApp(string[] args) {
        var builder = WebApplication
            .CreateBuilder(args);

      

        //use autofac for DI
        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

        //use serilog for logging
        builder.Host.UseSerilog();


        Console.WriteLine(builder.Configuration.GetConnectionString("Replica"));
        Console.WriteLine(builder.Configuration.GetConnectionString("Replica"));
        Console.WriteLine(builder.Configuration.GetConnectionString("Replica"));

        Console.WriteLine(builder.Configuration.GetConnectionString("Replica"));


        //upgrade replica db
        var upgradeReplicaDb =
            DeployChanges.To
                .SqlDatabase(builder.Configuration.GetConnectionString("Replica"))
                .WithScriptsFromFileSystem("SqlUpgradeScripts/Replica")
                .LogToConsole()
                .Build();

        var result = upgradeReplicaDb.PerformUpgrade();

        if (!result.Successful) {
            Log.Logger.Fatal($"Failed to upgrade replica db {result.Error}");
            return -1;
        }

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
        return 0;
    }
}