using System.Runtime.InteropServices;
using FluentAssertions;

namespace ExampleApp
{
    public static class Extensions {
        /// <summary>
        /// Assumes windows auth for windows env, and sql auth for anything else
        /// </summary>
        /// <param name="config"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string GetConnectionStringDependingOnOS(this IConfiguration config, string name)  =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? config.GetConnectionString(name)
                : config.GetConnectionStringWithSqlLogin(name);

        /// <summary>
        /// Gets a connection string from config and injects the configured value for sqluserid + sqlpassword
        /// </summary>
        /// <param name="config"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string GetConnectionStringWithSqlLogin(this IConfiguration config, string name) {
            var templatedConnectionString = config.GetConnectionString(name);
            var userId =Environment.GetEnvironmentVariable("SQLUSERID");
            var password = Environment.GetEnvironmentVariable("SQLPASSWORD");
            Console.WriteLine(userId);
            Console.WriteLine(userId);

            Console.WriteLine(userId);

            Console.WriteLine(userId);

            userId.Should().NotBeNullOrWhiteSpace($"need a config setting for sqluserid but found {userId}");
            password.Should().NotBeNullOrWhiteSpace("need a config setting for sqlpassword");
            return templatedConnectionString
                .Replace("{sqluserid}", userId)
                .Replace("{sqluserpassword}", password);
        }
    }
}
