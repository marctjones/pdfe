using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfEditor.Services;
using PdfEditor.ViewModels;
using PdfEditor.Views;
using ReactiveUI;
using System;

namespace PdfEditor;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Configure ReactiveUI to use Avalonia's dispatcher
        RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;

        // Configure dependency injection and logging
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        logger.LogInformation("=================================================");
        logger.LogInformation("PDF Editor Application Starting");
        logger.LogInformation("=================================================");
        logger.LogInformation("Framework initialization completed");
        logger.LogInformation("ReactiveUI configured to use Avalonia scheduler");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            logger.LogInformation("Creating main window");

            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>(),
            };

            logger.LogInformation("Main window created successfully");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configure logging with TRACE level for maximum verbosity
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Trace);

            // Configure console formatter for better readability
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = false;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
        });

        // Explicitly register ILoggerFactory as singleton (required by RedactionService)
        services.AddSingleton<ILoggerFactory, LoggerFactory>();

        // Register services
        services.AddSingleton<PdfDocumentService>();
        services.AddSingleton<PdfRenderService>();
        services.AddSingleton<RedactionService>();
        services.AddSingleton<PdfTextExtractionService>();
        services.AddSingleton<PdfSearchService>();

        // Register ViewModels
        services.AddTransient<MainWindowViewModel>();

        var tempProvider = services.BuildServiceProvider();
        var logger = tempProvider.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Dependency injection container configured");
        logger.LogInformation("Services registered: PdfDocumentService, PdfRenderService, RedactionService, PdfTextExtractionService");
        logger.LogInformation("Logging level set to: TRACE (maximum verbosity)");
    }
}
