using System.Configuration;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Dapper;
using DbUp;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
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

        // upgrade db
        if (!UpgradeDb(builder.Configuration.GetConnectionString("Replica") ?? throw new ConfigurationErrorsException("Replica connection string not set"), "SqlUpgradeScripts/Replica")) return -1;
        if (!UpgradeDb(builder.Configuration.GetConnectionString("CommitStore") ?? throw new ConfigurationErrorsException("CommitStore connection string not set\""), "SqlUpgradeScripts/CommitStore")) return -1;

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

    private static bool UpgradeDb(string connectionString, string fileSystemLocationForUpgradeScripts) {
        var upgrade = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsFromFileSystem(fileSystemLocationForUpgradeScripts)
            .WithTransactionPerScript()
            .LogToAutodetectedLog()
            .Build();

        var result = upgrade.PerformUpgrade();

        if (!result.Successful) { Log.Logger.Fatal($"Failed to upgrade db {result.Error}");
           return false;
        }

        return true;
    }
}