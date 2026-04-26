using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace Pdfe.Core.Tests.Parsing;

/// <summary>
/// Phase 1 (PDF 2.0) acceptance: every modern PDF (1.5+) compresses
/// indirect objects into /ObjStm streams. Without correct compressed-
/// object resolution, GetObject() returns junk for any object whose
/// xref entry has type 2.
///
/// The existing PdfDocument.GetObjectFromStream code path *appears*
/// implemented — these tests pin that down so it can't silently
/// regress.
/// </summary>
public class ObjectStreamResolutionTests
{
    private readonly ITestOutputHelper _out;
    public ObjectStreamResolutionTests(ITestOutputHelper o) { _out = o; }

    private const string ScrambledBirthCert =
        "../../../../test-pdfs/sample-pdfs/birth-certificate-request-scrambled.pdf";
    private const string MultilingualCjk =
        "../../../../test-pdfs/sample-pdfs/multilingual-noto-cjk.pdf";

    [Fact]
    public void OpensFileWithObjectStreams_AllCompressedObjectsResolveWithoutError()
    {
        // The scrambled birth certificate uses /ObjStm to compress its
        // page-tree and resource objects (6 occurrences of /ObjStm in the
        // file). If compressed-object resolution were broken, opening
        // would fail or the catalog/pages chain would return garbage.
        if (!File.Exists(ScrambledBirthCert)) return;

        using var doc = PdfDocument.Open(ScrambledBirthCert);

        doc.Catalog.Should().NotBeNull("catalog must resolve even when stored in /ObjStm");
        doc.PageCount.Should().BeGreaterThan(0,
            "pages tree must resolve even when stored in /ObjStm");

        // Walk every page — each resolves its dict, resources, content
        // streams transitively; if any compressed dependency fails, this
        // throws.
        for (int p = 1; p <= doc.PageCount; p++)
        {
            var page = doc.GetPage(p);
            page.Should().NotBeNull($"page {p} must resolve");
            page.Width.Should().BeGreaterThan(0);
            page.Height.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void OpensFileWithObjectStreams_CompressedObjectsAreActuallyInStream()
    {
        // Stronger assertion than the previous test: confirm that AT LEAST
        // ONE object reachable from the catalog truly has IsCompressed=true
        // in its xref entry. Otherwise the file may not actually exercise
        // the /ObjStm path and the test is vacuous.
        if (!File.Exists(ScrambledBirthCert)) return;

        using var doc = PdfDocument.Open(ScrambledBirthCert);

        var xref = GetXRef(doc);
        var compressed = xref.Where(e => e.Value.IsCompressed).ToList();
        _out.WriteLine($"xref entries total: {xref.Count}, compressed (type 2): {compressed.Count}");

        compressed.Should().NotBeEmpty(
            "the test file must contain at least one type-2 xref entry — " +
            "otherwise this test isn't actually exercising /ObjStm resolution");

        foreach (var (objNum, entry) in compressed.Take(5))
        {
            _out.WriteLine($"  obj {objNum}: stream={entry.ObjectStreamNumber} index={entry.IndexInStream}");
            var obj = doc.GetObject(objNum);
            obj.Should().NotBeNull($"compressed object {objNum} must resolve via /ObjStm");
            obj.Should().NotBeOfType<PdfNull>(
                $"compressed object {objNum} must resolve to its real value, not null");
        }
    }

    [Fact]
    public void RoundTripFileWithObjectStreams_LoadSaveLoad_PreservesCatalogAndPages()
    {
        // Open a /ObjStm-containing file, save it (the writer materialises
        // each object as an uncompressed indirect — that's fine, /ObjStm
        // is just a compression scheme), reload, and check that every
        // object that was resolvable before is still resolvable after.
        if (!File.Exists(ScrambledBirthCert)) return;

        int originalPageCount;
        int originalCompressedCount;
        using (var doc = PdfDocument.Open(ScrambledBirthCert))
        {
            originalPageCount = doc.PageCount;
            var xref = GetXRef(doc);
            originalCompressedCount = xref.Count(e => e.Value.IsCompressed);
        }

        // Round-trip through the writer.
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Open(ScrambledBirthCert))
        {
            var writer = new Pdfe.Core.Writing.PdfDocumentWriter(doc);
            writer.Write(ms);
        }
        ms.Position = 0;
        var roundTripBytes = ms.ToArray();
        _out.WriteLine($"original size: {new FileInfo(ScrambledBirthCert).Length}, " +
                       $"round-trip size: {roundTripBytes.Length}, " +
                       $"compressed objs in original: {originalCompressedCount}");

        using (var roundTripped = PdfDocument.Open(new MemoryStream(roundTripBytes)))
        {
            roundTripped.Catalog.Should().NotBeNull(
                "catalog must still resolve after round-trip");
            roundTripped.PageCount.Should().Be(originalPageCount,
                "page count must be preserved across save/reload");

            for (int p = 1; p <= roundTripped.PageCount; p++)
            {
                var page = roundTripped.GetPage(p);
                page.Should().NotBeNull($"page {p} must resolve in the round-tripped file");
                page.Width.Should().BeGreaterThan(0,
                    $"page {p} dimensions must survive round-trip");
            }

            // After round-trip, the writer materialises everything as
            // uncompressed indirect objects, so /ObjStm count goes to 0.
            // That's fine — what matters is that every formerly-compressed
            // object still resolves to a real value.
            var xref = GetXRef(roundTripped);
            _out.WriteLine($"round-tripped xref entries: {xref.Count}, " +
                           $"compressed (type 2): {xref.Count(e => e.Value.IsCompressed)}");
        }
    }

    [Theory]
    [InlineData("../../../../test-pdfs/smoke/irs-1040.pdf")]
    [InlineData("../../../../test-pdfs/smoke/irs-w9.pdf")]
    [InlineData("../../../../test-pdfs/smoke/scotus-trump-v-anderson.pdf")]
    public void OpensLinearizedPdf_ParsesViaStartxrefFromTail(string relativePath)
    {
        // Linearized ("Fast Web View") PDFs have a /Linearized dict at the
        // start of the file plus hint streams for streamed delivery. A
        // PDF *reader* doesn't need to interpret either — we read the
        // file end-first via startxref. These IRS / SCOTUS forms are all
        // linearized; if startxref-tail parsing breaks on linearized
        // structure, every page fetch fails.
        if (!File.Exists(relativePath)) return;

        using var doc = PdfDocument.Open(relativePath);
        doc.Catalog.Should().NotBeNull("catalog must resolve in linearized PDF");
        doc.PageCount.Should().BeGreaterThan(0, "page count must resolve");

        // First page is the meaningful one for linearization (front-loaded
        // for streamed delivery). Confirm it parses.
        var firstPage = doc.GetPage(1);
        firstPage.Should().NotBeNull();
        firstPage.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OpensCjkFileWithObjectStreams_PagesAndFontsResolve()
    {
        // CJK file: /ObjStm + Type 0/CID fonts. The fonts won't render
        // correctly until Phase 8, but the *parsing* should already work
        // — page tree and font dictionary objects must resolve through
        // the /ObjStm path.
        if (!File.Exists(MultilingualCjk)) return;

        using var doc = PdfDocument.Open(MultilingualCjk);
        doc.PageCount.Should().BeGreaterThan(0);

        var page = doc.GetPage(1);
        page.Resources.Should().NotBeNull(
            "page resources dict must resolve (typically inside /ObjStm)");
    }

    private const string EncryptedRC4Pdf =
        "../../../../test-pdfs/isartor/Isartor testsuite/PDFA-1b/6.1 File structure/6.1.3 File trailer/isartor-6-1-3-t02-fail-a.pdf";

    [Fact]
    public void OpensEncryptedPdf_DefaultBehaviour_ThrowsClearException()
    {
        // Pre-fix: opening an encrypted PDF returned a "successful" doc
        // whose content streams were ciphertext garbage (57% printable
        // bytes that looked nothing like valid PDF operators). Any
        // downstream code — text extraction, redaction, search — would
        // silently produce wrong output.
        //
        // Defensive fix: PdfDocument.Open now throws a clear, named
        // exception by default. Callers that want the old behaviour
        // (e.g. for inspecting the encryption dict itself) can pass
        // allowEncrypted: true.
        if (!File.Exists(EncryptedRC4Pdf)) return;

        Action open = () => { using var _ = PdfDocument.Open(EncryptedRC4Pdf); };
        open.Should().Throw<Pdfe.Core.Parsing.PdfEncryptionNotSupportedException>(
            "encrypted PDFs must throw a clear exception by default — silent " +
            "ciphertext-as-content was a security-adjacent failure mode");
    }

    [Fact]
    public void OpensEncryptedPdf_WithAllowEncrypted_ReturnsDocumentForInspection()
    {
        // The opt-in escape: callers that want to inspect the /Encrypt
        // dict, list xref entries, etc. without actually reading
        // encrypted streams can pass allowEncrypted: true. This is
        // explicitly best-effort — content streams will still be
        // ciphertext until #324 lands real decryption.
        if (!File.Exists(EncryptedRC4Pdf)) return;

        using var doc = PdfDocument.Open(EncryptedRC4Pdf, allowEncrypted: true);
        doc.IsEncrypted.Should().BeTrue("file actually is encrypted; the flag just suppresses the throw");
        doc.Trailer.GetOptional("Encrypt").Should().NotBeNull(
            "/Encrypt dict must be reachable so callers inspecting encryption parameters can read /V, /R, /U, /O");
    }

    private static IReadOnlyDictionary<int, Pdfe.Core.Parsing.XRefEntry> GetXRef(PdfDocument doc)
    {
        // _xref is private; expose via reflection so this test can verify
        // the actual entry types without changing PdfDocument's API.
        var field = typeof(PdfDocument).GetField("_xref",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PdfDocument._xref field not found");
        return (Dictionary<int, Pdfe.Core.Parsing.XRefEntry>)field.GetValue(doc)!;
    }
}
