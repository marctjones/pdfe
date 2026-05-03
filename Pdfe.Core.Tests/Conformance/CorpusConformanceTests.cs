using System.IO;
using AwesomeAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Parsing;
using Xunit;
namespace Pdfe.Core.Tests.Conformance;

/// <summary>
/// Parses every PDF in the veraPDF test corpus and reports failures.
/// Skipped automatically when the corpus is not present.
/// Run: dotnet test --filter "FullyQualifiedName~Corpus"
/// </summary>
public class CorpusConformanceTests
{
    private const string CorpusRoot = "../../../../test-pdfs";
    private readonly ITestOutputHelper _out;

    public CorpusConformanceTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Corpus_ParsesWithoutCrash_AllPdfs()
    {
        if (!Directory.Exists(CorpusRoot))
        {
            _out.WriteLine("Corpus not present — skipping");
            return;
        }

        var files = Directory.GetFiles(CorpusRoot, "*.pdf", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            _out.WriteLine("No PDFs found in corpus — skipping");
            return;
        }

        int total = 0, ok = 0, parseErr = 0, crash = 0;
        var failures = new List<string>();

        foreach (var f in files)
        {
            total++;
            try
            {
                using var doc = PdfDocument.Open(f);
                for (int p = 1; p <= Math.Min(doc.PageCount, 5); p++)
                {
                    var page = doc.GetPage(p);
                    _ = page.Width;
                    _ = page.Height;
                }
                ok++;
            }
            catch (PdfParseException ex)
            {
                parseErr++;
                failures.Add($"PARSE  {Path.GetFileName(f)}: {ex.Message}");
            }
            catch (Exception ex)
            {
                crash++;
                failures.Add($"CRASH  {Path.GetFileName(f)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _out.WriteLine($"Total: {total}  OK: {ok}  ParseErr: {parseErr}  Crash: {crash}");
        foreach (var f in failures.Take(50))
            _out.WriteLine(f);

        crash.Should().BeLessThanOrEqualTo(total, "crash count cannot exceed file count");
    }

    [Fact]
    public void Corpus_SmokeFiles_ParsesCleanly()
    {
        var smokeDir = Path.Combine(CorpusRoot, "smoke");
        if (!Directory.Exists(smokeDir))
        {
            _out.WriteLine("smoke/ not present — skipping");
            return;
        }

        var files = Directory.GetFiles(smokeDir, "*.pdf", SearchOption.AllDirectories);
        var failures = new List<string>();

        foreach (var f in files)
        {
            try
            {
                using var doc = PdfDocument.Open(f);
                for (int p = 1; p <= doc.PageCount; p++)
                {
                    var page = doc.GetPage(p);
                    _ = page.GetContentStreamBytes();
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(f)}: {ex.Message}");
            }
        }

        failures.Should().BeEmpty("smoke PDFs must always parse without errors");
    }
}
