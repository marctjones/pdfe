using PdfSharp.Fonts;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = false)]
[assembly: TestFramework("PdfEditor.Tests.Utilities.TestSetup", "PdfEditor.Tests")]

namespace PdfEditor.Tests.Utilities;

/// <summary>
/// Test setup that initializes the font resolver before any tests run.
/// This ensures PdfSharpCore can find fonts on all platforms (Windows, Linux, macOS).
/// </summary>
public class TestSetup : Xunit.Sdk.XunitTestFramework
{
    public TestSetup(Xunit.Abstractions.IMessageSink messageSink)
        : base(messageSink)
    {
        // Initialize font resolver for all tests
        // Use TestFontResolver instead of CustomFontResolver for performance
        // TestFontResolver uses a single cached font vs scanning 2,967 system fonts
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new TestFontResolver();
        }
    }
}
