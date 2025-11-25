using Avalonia;
using Avalonia.Headless;

namespace PdfEditor.Tests.UI;

/// <summary>
/// Application builder for headless UI tests
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Minimal Avalonia application for testing
/// </summary>
public class TestApp : Application
{
    public override void Initialize()
    {
        // Minimal initialization for headless testing
    }
}
