using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using Xunit;
using Xunit.Sdk;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Tests for CJK (Chinese, Japanese, Korean) font redaction.
/// Issue #63: CID/CJK font support.
/// </summary>
public class CjkFontRedactionTests
{
    private const string CidFontsCorpusPath =
        "/home/marc/pdfe/test-pdfs/verapdf-corpus/veraPDF-corpus-master/PDF_UA-1/7.21 Fonts/7.21.3 Composite fonts/7.21.3.2 CIDFonts";

    private const string PdfA2bCidFontsPath =
        "/home/marc/pdfe/test-pdfs/verapdf-corpus/veraPDF-corpus-master/PDF_A-2b/6.2 Graphics/6.2.11 Fonts/6.2.11.3 Composite fonts/6.2.11.3.2 CIDFonts";

    private const string PdfA4CidFontsPath =
        "/home/marc/pdfe/test-pdfs/verapdf-corpus/veraPDF-corpus-master/PDF_A-4/6.2 Graphics/6.2.10 Fonts/6.2.10.3 Composite fonts/6.2.10.3.2 CIDFonts";

    [SkippableFact]
    [Trait("Category", "CJK")]
    public void ChineseText_WithCidFont_CanBeRedacted()
    {
        // Arrange
        var pdfPath = Path.Combine(CidFontsCorpusPath, "7.21.3.2-t01-pass-a.pdf");
        Skip.If(!File.Exists(pdfPath), "CID font test PDF not available");

        var outputPath = Path.Combine(Path.GetTempPath(), $"cjk_redaction_{Guid.NewGuid()}.pdf");

        try
        {
            // Verify original has Chinese text
            var originalText = PdfTestHelpers.ExtractAllText(pdfPath);
            originalText.Should().Contain("便携式", "Original should contain Chinese text");

            // Act - redact Chinese substring
            var redactor = new TextRedactor();
            redactor.RedactText(pdfPath, outputPath, "便携式");

            // Assert
            var redactedText = PdfTestHelpers.ExtractAllText(outputPath);
            redactedText.Should().NotContain("便携式", "Chinese text should be removed");
            redactedText.Should().Contain("文件格式", "Remaining Chinese text should be preserved");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [SkippableFact]
    [Trait("Category", "CJK")]
    public void ChineseText_FullLine_CanBeRedacted()
    {
        // Arrange
        var pdfPath = Path.Combine(CidFontsCorpusPath, "7.21.3.2-t01-pass-a.pdf");
        Skip.If(!File.Exists(pdfPath), "CID font test PDF not available");

        var outputPath = Path.Combine(Path.GetTempPath(), $"cjk_full_redaction_{Guid.NewGuid()}.pdf");

        try
        {
            // Verify original has Chinese text
            var originalText = PdfTestHelpers.ExtractAllText(pdfPath);
            originalText.Should().Contain("便携式文件格式", "Original should contain full Chinese phrase");

            // Act - redact the full Chinese phrase
            var redactor = new TextRedactor();
            redactor.RedactText(pdfPath, outputPath, "便携式文件格式");

            // Assert
            var redactedText = PdfTestHelpers.ExtractAllText(outputPath);
            redactedText.Should().NotContain("便携式文件格式", "Full Chinese phrase should be removed");
            redactedText.Should().NotContain("便携式", "First part should be removed");
            redactedText.Should().NotContain("文件格式", "Second part should be removed");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [SkippableFact]
    [Trait("Category", "CJK")]
    public void MultipleCjkPdfs_CanBeRedacted()
    {
        // Test multiple CJK PDFs from the corpus
        var testCases = new[]
        {
            (Path: Path.Combine(CidFontsCorpusPath, "7.21.3.2-t01-pass-a.pdf"), Text: "便携式"),
            (Path: Path.Combine(PdfA4CidFontsPath, "veraPDF test suite 6-2-10-3-2-t01-pass-a.pdf"), Text: "便携式"),
        };

        foreach (var (path, text) in testCases)
        {
            Skip.If(!File.Exists(path), $"CID font test PDF not available: {path}");

            var outputPath = Path.Combine(Path.GetTempPath(), $"cjk_multi_{Guid.NewGuid()}.pdf");

            try
            {
                // Verify original has the text
                var originalText = PdfTestHelpers.ExtractAllText(path);
                if (!originalText.Contains(text))
                {
                    // Some PDFs may not have this text
                    continue;
                }

                // Act
                var redactor = new TextRedactor();
                redactor.RedactText(path, outputPath, text);

                // Assert
                var redactedText = PdfTestHelpers.ExtractAllText(outputPath);
                redactedText.Should().NotContain(text, $"Text '{text}' should be removed from {Path.GetFileName(path)}");
            }
            finally
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
        }
    }

    [SkippableFact]
    [Trait("Category", "CJK")]
    public void PdfA2b_CidFont_CanBeRedacted()
    {
        // Arrange
        var pdfPath = Path.Combine(PdfA2bCidFontsPath, "veraPDF test suite 6-2-11-3-2-t01-pass-a.pdf");
        Skip.If(!File.Exists(pdfPath), "PDF/A-2b CID font test PDF not available");

        var outputPath = Path.Combine(Path.GetTempPath(), $"pdfa2b_cjk_{Guid.NewGuid()}.pdf");

        try
        {
            var originalText = PdfTestHelpers.ExtractAllText(pdfPath);

            // Find any Chinese character sequence to redact
            var chineseMatch = System.Text.RegularExpressions.Regex.Match(originalText, @"[\u4e00-\u9fff]+");
            Skip.If(!chineseMatch.Success, "No Chinese text found in PDF");

            var textToRedact = chineseMatch.Value.Substring(0, Math.Min(3, chineseMatch.Value.Length));

            // Act
            var redactor = new TextRedactor();
            redactor.RedactText(pdfPath, outputPath, textToRedact);

            // Assert
            var redactedText = PdfTestHelpers.ExtractAllText(outputPath);
            redactedText.Should().NotContain(textToRedact, "Chinese text should be removed");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [SkippableFact]
    [Trait("Category", "CJK")]
    public void PdfA4_CidFont_CanBeRedacted()
    {
        // Arrange
        var pdfPath = Path.Combine(PdfA4CidFontsPath, "veraPDF test suite 6-2-10-3-2-t01-pass-a.pdf");
        Skip.If(!File.Exists(pdfPath), "PDF/A-4 CID font test PDF not available");

        var outputPath = Path.Combine(Path.GetTempPath(), $"pdfa4_cjk_{Guid.NewGuid()}.pdf");

        try
        {
            var originalText = PdfTestHelpers.ExtractAllText(pdfPath);

            // Find any Chinese character sequence to redact
            var chineseMatch = System.Text.RegularExpressions.Regex.Match(originalText, @"[\u4e00-\u9fff]+");
            Skip.If(!chineseMatch.Success, "No Chinese text found in PDF");

            var textToRedact = chineseMatch.Value.Substring(0, Math.Min(3, chineseMatch.Value.Length));

            // Act
            var redactor = new TextRedactor();
            redactor.RedactText(pdfPath, outputPath, textToRedact);

            // Assert
            var redactedText = PdfTestHelpers.ExtractAllText(outputPath);
            redactedText.Should().NotContain(textToRedact, "Chinese text should be removed");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}
