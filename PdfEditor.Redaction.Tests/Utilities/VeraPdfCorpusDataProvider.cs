namespace PdfEditor.Redaction.Tests.Utilities;

/// <summary>
/// Provides veraPDF corpus test files as xUnit MemberData for Theory tests.
///
/// Usage:
/// <code>
/// [Theory]
/// [MemberData(nameof(VeraPdfCorpusDataProvider.GetFontTestFiles), MemberType = typeof(VeraPdfCorpusDataProvider))]
/// public void FontTest_FromCorpus(string pdfPath, string displayName)
/// {
///     // Test logic
/// }
/// </code>
/// </summary>
public static class VeraPdfCorpusDataProvider
{
    private static readonly string[] CorpusSearchPaths = new[]
    {
        "/home/marc/pdfe/test-pdfs/verapdf-corpus/veraPDF-corpus-master",
        Path.Combine(Directory.GetCurrentDirectory(), "test-pdfs", "verapdf-corpus", "veraPDF-corpus-master"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "test-pdfs", "verapdf-corpus", "veraPDF-corpus-master"),
    };

    private static string? _corpusPath;

    /// <summary>
    /// Get the path to the veraPDF corpus root directory.
    /// Returns null if corpus is not installed.
    /// </summary>
    public static string? CorpusPath
    {
        get
        {
            if (_corpusPath == null)
            {
                _corpusPath = CorpusSearchPaths.FirstOrDefault(Directory.Exists);
            }
            return _corpusPath;
        }
    }

    /// <summary>
    /// Check if the veraPDF corpus is available.
    /// </summary>
    public static bool IsCorpusAvailable => CorpusPath != null;

    // =====================================================================
    // PDF/A Categories
    // =====================================================================

    /// <summary>PDF/A-1b category (archival, basic conformance).</summary>
    public const string PdfA1b = "PDF_A-1b";

    /// <summary>PDF/A-2b category (enhanced archival, basic conformance).</summary>
    public const string PdfA2b = "PDF_A-2b";

    /// <summary>PDF/A-3b category (archival with attachments, basic conformance).</summary>
    public const string PdfA3b = "PDF_A-3b";

    /// <summary>PDF/A-4 category (latest archival based on PDF 2.0).</summary>
    public const string PdfA4 = "PDF_A-4";

    // =====================================================================
    // PDF/A-1b Subcategories (for atomic testing)
    // See: https://github.com/veraPDF/veraPDF-corpus/tree/master/PDF_A-1b
    // =====================================================================

    /// <summary>6.1 File structure tests.</summary>
    public const string FileStructure = "6.1 File structure";

    /// <summary>6.2 Graphics tests.</summary>
    public const string Graphics = "6.2 Graphics";

    /// <summary>6.3 Fonts tests.</summary>
    public const string Fonts = "6.3 Fonts";

    /// <summary>6.4 Transparency tests.</summary>
    public const string Transparency = "6.4 Transparency";

    /// <summary>6.5 Annotations tests.</summary>
    public const string Annotations = "6.5 Annotations";

    /// <summary>6.6 Actions tests.</summary>
    public const string Actions = "6.6 Actions";

    /// <summary>6.7 Metadata tests.</summary>
    public const string Metadata = "6.7 Metadata";

    /// <summary>6.9 Interactive Forms tests.</summary>
    public const string InteractiveForms = "6.9 Interactive Forms";

    // =====================================================================
    // Core Data Providers
    // =====================================================================

    /// <summary>
    /// Get all PDF files in a specific category.
    /// </summary>
    /// <param name="category">Category path relative to corpus root (e.g., "PDF_A-1b").</param>
    /// <param name="maxFiles">Maximum number of files to return (0 = unlimited).</param>
    /// <returns>MemberData enumerable of (pdfPath, displayName) tuples.</returns>
    public static IEnumerable<object[]> GetFilesInCategory(string category, int maxFiles = 0)
    {
        if (!IsCorpusAvailable)
        {
            yield return new object[] { $"SKIP: Corpus not available", "corpus_not_available" };
            yield break;
        }

        var categoryPath = Path.Combine(CorpusPath!, category);
        if (!Directory.Exists(categoryPath))
        {
            yield return new object[] { $"SKIP: Category {category} not found", $"category_{category}_not_found" };
            yield break;
        }

        var files = Directory.GetFiles(categoryPath, "*.pdf", SearchOption.AllDirectories);
        var fileList = maxFiles > 0 ? files.Take(maxFiles) : files;

        foreach (var file in fileList)
        {
            yield return new object[] { file, GetDisplayName(file, categoryPath) };
        }
    }

    /// <summary>
    /// Get all PDF files in a PDF/A-1b subcategory.
    /// </summary>
    /// <param name="subcategory">Subcategory path (e.g., "6.3 Fonts").</param>
    /// <param name="maxFiles">Maximum number of files to return (0 = unlimited).</param>
    public static IEnumerable<object[]> GetPdfA1bSubcategory(string subcategory, int maxFiles = 0)
    {
        return GetFilesInCategory(Path.Combine(PdfA1b, subcategory), maxFiles);
    }

    // =====================================================================
    // Atomic Test Data Providers
    // =====================================================================

    /// <summary>
    /// Get font test files from PDF/A-1b corpus (6.3 Fonts category).
    /// For Issue #139: Font Preservation Tests.
    /// </summary>
    public static IEnumerable<object[]> GetFontTestFiles(int maxFiles = 50)
    {
        return GetPdfA1bSubcategory(Fonts, maxFiles);
    }

    /// <summary>
    /// Get content stream test files from PDF/A-1b corpus.
    /// For Issue #140: Content Stream Handling Tests.
    /// Covers 6.1 File structure and 6.2 Graphics.
    /// </summary>
    public static IEnumerable<object[]> GetContentStreamTestFiles(int maxFiles = 50)
    {
        if (!IsCorpusAvailable)
        {
            yield return new object[] { "SKIP: Corpus not available", "corpus_not_available" };
            yield break;
        }

        var categories = new[] { FileStructure, Graphics };
        var files = new List<string>();

        foreach (var cat in categories)
        {
            var categoryPath = Path.Combine(CorpusPath!, PdfA1b, cat);
            if (Directory.Exists(categoryPath))
            {
                files.AddRange(Directory.GetFiles(categoryPath, "*.pdf", SearchOption.AllDirectories));
            }
        }

        var fileList = maxFiles > 0 ? files.Take(maxFiles) : files;
        foreach (var file in fileList)
        {
            yield return new object[] { file, GetDisplayName(file, Path.Combine(CorpusPath!, PdfA1b)) };
        }
    }

    /// <summary>
    /// Get PDF/A compliance test files.
    /// For Issue #142: PDF/A Compliance Tests.
    /// </summary>
    public static IEnumerable<object[]> GetPdfAComplianceTestFiles(int maxFiles = 50)
    {
        if (!IsCorpusAvailable)
        {
            yield return new object[] { "SKIP: Corpus not available", "corpus_not_available" };
            yield break;
        }

        // Get files from all PDF/A variants
        var categories = new[] { PdfA1b, PdfA2b, PdfA3b, PdfA4 };
        var files = new List<string>();

        foreach (var cat in categories)
        {
            var categoryPath = Path.Combine(CorpusPath!, cat);
            if (Directory.Exists(categoryPath))
            {
                // Take a sample from each
                files.AddRange(Directory.GetFiles(categoryPath, "*pass*.pdf", SearchOption.AllDirectories).Take(10));
            }
        }

        var fileList = maxFiles > 0 ? files.Take(maxFiles) : files;
        foreach (var file in fileList)
        {
            yield return new object[] { file, GetDisplayName(file, CorpusPath!) };
        }
    }

    /// <summary>
    /// Get form field test files.
    /// For Issue #143: Form Field Handling Tests.
    /// </summary>
    public static IEnumerable<object[]> GetFormFieldTestFiles(int maxFiles = 50)
    {
        return GetPdfA1bSubcategory(InteractiveForms, maxFiles);
    }

    /// <summary>
    /// Get transparency test files.
    /// For transparency handling tests (PDF/A-1 forbids transparency).
    /// </summary>
    public static IEnumerable<object[]> GetTransparencyTestFiles(int maxFiles = 50)
    {
        return GetPdfA1bSubcategory(Transparency, maxFiles);
    }

    /// <summary>
    /// Get annotation test files.
    /// For annotation redaction tests.
    /// </summary>
    public static IEnumerable<object[]> GetAnnotationTestFiles(int maxFiles = 50)
    {
        return GetPdfA1bSubcategory(Annotations, maxFiles);
    }

    /// <summary>
    /// Get files that should pass PDF/A validation.
    /// These are "pass" test cases from the corpus.
    /// </summary>
    public static IEnumerable<object[]> GetPassingTestFiles(string category, int maxFiles = 50)
    {
        if (!IsCorpusAvailable)
        {
            yield return new object[] { "SKIP: Corpus not available", "corpus_not_available" };
            yield break;
        }

        var categoryPath = Path.Combine(CorpusPath!, category);
        if (!Directory.Exists(categoryPath))
        {
            yield return new object[] { $"SKIP: Category {category} not found", $"category_{category}_not_found" };
            yield break;
        }

        var files = Directory.GetFiles(categoryPath, "*pass*.pdf", SearchOption.AllDirectories);
        var fileList = maxFiles > 0 ? files.Take(maxFiles) : files;

        foreach (var file in fileList)
        {
            yield return new object[] { file, GetDisplayName(file, categoryPath) };
        }
    }

    /// <summary>
    /// Get files that should fail PDF/A validation.
    /// These are "fail" test cases from the corpus.
    /// </summary>
    public static IEnumerable<object[]> GetFailingTestFiles(string category, int maxFiles = 50)
    {
        if (!IsCorpusAvailable)
        {
            yield return new object[] { "SKIP: Corpus not available", "corpus_not_available" };
            yield break;
        }

        var categoryPath = Path.Combine(CorpusPath!, category);
        if (!Directory.Exists(categoryPath))
        {
            yield return new object[] { $"SKIP: Category {category} not found", $"category_{category}_not_found" };
            yield break;
        }

        var files = Directory.GetFiles(categoryPath, "*fail*.pdf", SearchOption.AllDirectories);
        var fileList = maxFiles > 0 ? files.Take(maxFiles) : files;

        foreach (var file in fileList)
        {
            yield return new object[] { file, GetDisplayName(file, categoryPath) };
        }
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>
    /// Get a display-friendly name for test output.
    /// </summary>
    private static string GetDisplayName(string filePath, string basePath)
    {
        var relativePath = Path.GetRelativePath(basePath, filePath);
        // Shorten for readability
        return relativePath.Length > 60
            ? "..." + relativePath.Substring(relativePath.Length - 57)
            : relativePath;
    }

    /// <summary>
    /// Check if a path is a skip marker (returned when corpus not available).
    /// Use this in tests to skip gracefully.
    /// </summary>
    public static bool IsSkipMarker(string path)
    {
        return path.StartsWith("SKIP:");
    }

    /// <summary>
    /// Get all available categories in the corpus.
    /// </summary>
    public static IEnumerable<string> GetAvailableCategories()
    {
        if (!IsCorpusAvailable)
            yield break;

        var dirs = Directory.GetDirectories(CorpusPath!);
        foreach (var dir in dirs)
        {
            yield return Path.GetFileName(dir);
        }
    }
}
