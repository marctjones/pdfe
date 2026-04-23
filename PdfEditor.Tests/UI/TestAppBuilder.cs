using Avalonia;
using Avalonia.Headless;

// Registers the headless app builder used by [AvaloniaFact] tests across the
// assembly. Required for UseHeadlessDrawing=false to take effect so that
// Bitmap(stream) decodes real pixels (visual-regression tests rely on this).
[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(PdfEditor.Tests.UI.TestAppBuilder))]

namespace PdfEditor.Tests.UI;

/// <summary>
/// Application builder for headless UI tests
/// </summary>
public class TestAppBuilder
{
    private static bool _isInitialized;
    private static readonly object _lock = new object();

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            // Route drawing through the real Skia backend so that Bitmap(stream)
            // decodes to the correct dimensions (default null renderer returns 1x1),
            // enabling visual-regression tests to capture actual rendered pixels.
            UseHeadlessDrawing = false
        });

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
