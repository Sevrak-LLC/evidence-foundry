using EvidenceFoundry.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace EvidenceFoundry;

internal sealed class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var logInitializationError = InitializeLogging(out var configuration);
        using var services = BuildServices(configuration);
        var logger = services.GetRequiredService<ILogger<Program>>();

        if (logInitializationError != null)
        {
            logger.LogWarning(
                logInitializationError,
                "Logging failed to initialize. Continuing without file logging.");
        }

        Application.ThreadException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled UI thread exception.");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                logger.LogCritical(exception, "Unhandled non-UI exception.");
            }
            else
            {
                logger.LogCritical("Unhandled non-UI exception: {ExceptionObject}", args.ExceptionObject);
            }
        };

        logger.LogInformation("EvidenceFoundry starting up. Version {Version}.", Application.ProductVersion);

        try
        {
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

    private static Exception? InitializeLogging(out IConfiguration configuration)
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        var logFilePath = Path.Combine(logDirectory, "evidencefoundry-.log");

        try
        {
            Directory.CreateDirectory(logDirectory);
            configuration = BuildConfiguration(logFilePath);
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            return null;
        }
        catch (Exception ex)
        {
            configuration = new ConfigurationBuilder().Build();
            Log.Logger = new LoggerConfiguration().MinimumLevel.Information().CreateLogger();
            MessageBox.Show(
                $"Logging could not initialize. Logs will not be written to disk.\n\n{ex.Message}\nPath: {logFilePath}",
                "Logging Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return ex;
        }
    }

    private static IConfiguration BuildConfiguration(string logFilePath)
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        var overrides = new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Args:path"] = logFilePath
        };

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "EVIDENCEFOUNDRY_")
            .AddInMemoryCollection(overrides)
            .Build();
    }

    private static ServiceProvider BuildServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });
        services.AddTransient<WizardForm>();
        return services.BuildServiceProvider();
    }
}
