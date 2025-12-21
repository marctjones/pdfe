using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Avalonia.Media;
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
        // Configure logging with INFO level to reduce noise
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);

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

        // Simple brush converter for status indicators
        services.AddSingleton<IBrushConverter, SimpleBooleanToBrushConverter>();

        // Register services
        services.AddSingleton<PdfDocumentService>();
        services.AddSingleton<PdfRenderService>();
        services.AddSingleton<RedactionService>();
        services.AddSingleton<PdfTextExtractionService>();
        services.AddSingleton<PdfSearchService>();
        services.AddSingleton<PdfOcrService>();
        services.AddSingleton<SignatureVerificationService>();
        services.AddSingleton<PdfEditor.Services.Verification.RedactionVerifier>();
        services.AddSingleton<FilenameSuggestionService>();

        // Register ViewModels
        services.AddTransient<MainWindowViewModel>();

        var tempProvider = services.BuildServiceProvider();
        var logger = tempProvider.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Dependency injection container configured");
        logger.LogInformation("Services registered: PdfDocumentService, PdfRenderService, RedactionService, PdfTextExtractionService");
        logger.LogInformation("Logging level set to: INFORMATION");
    }
}

public interface IBrushConverter
{
    IBrush Convert(bool value, string parameters);
}

public class SimpleBooleanToBrushConverter : IBrushConverter
{
    public IBrush Convert(bool value, string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return value ? Brushes.Green : Brushes.Transparent;

        var parts = parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var falseBrush = parts.Length > 0 ? parts[0] : "Transparent";
        var trueBrush = parts.Length > 1 ? parts[1] : "Green";

        return value ? Brush.Parse(trueBrush) : Brush.Parse(falseBrush);
    }
}

public class BooleanToBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not bool boolValue)
            return Brushes.Transparent;

        var paramString = parameter?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(paramString))
            return boolValue ? Brushes.Green : Brushes.Transparent;

        var parts = paramString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var trueBrush = parts.Length > 0 ? parts[0] : "Green";
        var falseBrush = parts.Length > 1 ? parts[1] : "Transparent";

        return boolValue ? Brush.Parse(trueBrush) : Brush.Parse(falseBrush);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
