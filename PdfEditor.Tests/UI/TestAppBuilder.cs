using Avalonia;
using Avalonia.Headless;

namespace PdfEditor.Tests.UI;

/// <summary>
/// Application builder for headless UI tests
/// </summary>
public class TestAppBuilder
{
    private static bool _isInitialized;
    private static readonly object _lock = new object();

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    /// <summary>
    /// Ensures Avalonia is initialized only once. Thread-safe.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_isInitialized) return;

        lock (_lock)
        {
            if (_isInitialized) return;

            try
            {
                BuildAvaloniaApp().SetupWithoutStarting();
                _isInitialized = true;
            }
            catch (InvalidOperationException)
            {
                // Already initialized by another test - this is fine
                _isInitialized = true;
            }
        }
    }
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
