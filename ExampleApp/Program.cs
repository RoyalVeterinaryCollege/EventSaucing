using Autofac;
using Autofac.Extensions.DependencyInjection;
using ExampleApp;

var builder = WebApplication
    .CreateBuilder(args);


//use autofac for DI
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());




// Add services to the container.
builder.Services.AddRazorPages();

var startup = new Startup(builder.Configuration);
startup.ConfigureServices(builder.Services);

// Register services directly with Autofac here. Don't
// call builder.Populate(), that happens in AutofacServiceProviderFactory.
builder.Host.ConfigureContainer<ContainerBuilder>(builder => startup.ConfigureContainer(builder));


var app = builder.Build();
startup.Configure(app, app.Environment);
app.Run();
