using PdfSharp.Fonts;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = false)]
[assembly: TestFramework("PdfEditor.Redaction.Tests.Utilities.TestSetup", "PdfEditor.Redaction.Tests")]

namespace PdfEditor.Redaction.Tests.Utilities;

/// <summary>
/// Test setup that initializes the font resolver before any tests run.
/// This ensures PDFsharp can find fonts on all platforms (Windows, Linux, macOS).
/// </summary>
public class TestSetup : Xunit.Sdk.XunitTestFramework
{
    public TestSetup(Xunit.Abstractions.IMessageSink messageSink)
        : base(messageSink)
    {
        // Initialize font resolver for all tests
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new TestFontResolver();
        }
    }
}
