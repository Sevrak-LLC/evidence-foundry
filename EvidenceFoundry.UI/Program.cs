using EvidenceFoundry.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvidenceFoundry;

internal sealed partial class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var configuration = BuildConfiguration();
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        try
        {
            using var services = BuildServices(configuration);
            var logger = services.GetRequiredService<ILogger>();

            Application.ThreadException += (_, args) =>
            {
                LogMessages.UnhandledUiThreadException(logger, args.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception exception)
                {
                    LogMessages.UnhandledNonUiException(logger, exception);
                }
                else
                {
                    LogMessages.UnhandledNonUiExceptionObject(logger, args.ExceptionObject);
                }
            };

            LogMessages.StartingUp(logger, Application.ProductVersion);

            // Show disclaimer dialog first
            using var disclaimer = new DisclaimerDialog();
            if (disclaimer.ShowDialog() != DialogResult.OK)
            {
                return; // User declined, exit application
            }

            Application.Run(services.GetRequiredService<WizardForm>());
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static class LogMessages
    {
        public static void UnhandledUiThreadException(ILogger logger, Exception exception)
            => logger.Error(exception, "Unhandled UI thread exception.");

        public static void UnhandledNonUiException(ILogger logger, Exception exception)
            => logger.Fatal(exception, "Unhandled non-UI exception.");

        public static void UnhandledNonUiExceptionObject(ILogger logger, object exceptionObject)
            => logger.Fatal("Unhandled non-UI exception: {ExceptionObject}", exceptionObject);

        public static void StartingUp(ILogger logger, string version)
            => logger.Information("EvidenceFoundry starting up. Version {Version}.", version);
    }

    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "EVIDENCEFOUNDRY_")
            .Build();
    }

    private static ServiceProvider BuildServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<ILogger>(Log.Logger);
        services.AddTransient<WizardForm>();
        return services.BuildServiceProvider();
    }
}
