using Autofac;
using Autofac.Extensions.DependencyInjection;
using ExampleApp;
using Serilog;

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
