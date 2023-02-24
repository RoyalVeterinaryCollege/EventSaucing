using Autofac;
using Serilog;

namespace ExampleApp.Modules
{
    public class LoggingModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.Register(c => Log.Logger).SingleInstance();

            // Required by EventSaucing.  NEventstore uses the MS ILogger interface
            var loggerFactory = (ILoggerFactory)new LoggerFactory();
            loggerFactory.AddSerilog(Log.Logger);
            var logger = loggerFactory.CreateLogger("default_logger");
            builder.Register(c => logger).SingleInstance();
        }
    }
}