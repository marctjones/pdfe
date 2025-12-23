using PdfSharp.Fonts;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = false)]
[assembly: TestFramework("PdfEditor.Redaction.Cli.Tests.TestHelpers.TestSetup", "PdfEditor.Redaction.Cli.Tests")]

namespace PdfEditor.Redaction.Cli.Tests.TestHelpers;

/// <summary>
/// Test setup that initializes the font resolver before any tests run.
/// </summary>
public class TestSetup : Xunit.Sdk.XunitTestFramework
{
    public TestSetup(Xunit.Abstractions.IMessageSink messageSink)
        : base(messageSink)
    {
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new TestFontResolver();
        }
    }
}
