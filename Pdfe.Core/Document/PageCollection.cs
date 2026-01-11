using Pdfe.Core.Primitives;
using System.Collections;

namespace Pdfe.Core.Document;

/// <summary>
/// Collection of pages in a PDF document.
/// Provides methods for adding, removing, inserting, and reordering pages.
/// </summary>
public class PageCollection : IReadOnlyList<PdfPage>
{
    private readonly PdfDocument _document;
    private readonly List<PdfPage> _pages;
    private readonly PdfDictionary _pagesDict;
    private PdfArray _kidsArray;

    /// <summary>
    /// Creates a new page collection for the document.
    /// </summary>
    internal PageCollection(PdfDocument document)
    {
        _document = document;
        _pages = new List<PdfPage>();

        // Get the Pages dictionary from catalog
        var pagesRef = document.Catalog.GetReferenceOrNull("Pages");
        if (pagesRef == null)
            throw new InvalidOperationException("Document has no Pages dictionary");

        _pagesDict = document.GetObject(pagesRef) as PdfDictionary
            ?? throw new InvalidOperationException("Pages is not a dictionary");

        // Get or create Kids array
        var kidsObj = _pagesDict.GetOptional("Kids");
        _kidsArray = kidsObj != null
            ? document.Resolve(kidsObj) as PdfArray ?? new PdfArray()
            : new PdfArray();

        // Load all pages (flatten page tree if necessary)
        LoadPages();
    }

    /// <summary>
    /// Load all pages from the page tree.
    /// </summary>
    private void LoadPages()
    {
        _pages.Clear();
        LoadPagesRecursive(_pagesDict, 0);
    }

    /// <summary>
    /// Recursively load pages from a Pages node.
    /// </summary>
    private int LoadPagesRecursive(PdfDictionary node, int pageNumber)
    {
        var type = node.GetNameOrNull("Type");

        if (type == "Page")
        {
            // This is a leaf page
            _pages.Add(new PdfPage(_document, node, pageNumber + 1));
            return 1;
        }

        // This is a Pages node
        var kids = node.GetArrayOrNull("Kids");
        if (kids == null)
            return 0;

        int count = 0;
        foreach (var kidRef in kids)
        {
            if (kidRef is not PdfReference kr)
                continue;

            var kid = _document.GetObject(kr) as PdfDictionary;
            if (kid == null)
                continue;

            count += LoadPagesRecursive(kid, pageNumber + count);
        }

        return count;
    }

    /// <summary>
    /// Number of pages in the collection.
    /// </summary>
    public int Count => _pages.Count;

    /// <summary>
    /// Get a page by index (0-based).
    /// </summary>
    public PdfPage this[int index]
    {
        get
        {
            if (index < 0 || index >= _pages.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Page index must be between 0 and {_pages.Count - 1}");
            return _pages[index];
        }
    }

    /// <summary>
    /// Add a page from another document to the end of this document.
    /// Creates a copy of the page.
    /// </summary>
    /// <param name="page">The page to add (can be from another document).</param>
    public void Add(PdfPage page)
    {
        Insert(_pages.Count, page);
    }

    /// <summary>
    /// Insert a page at the specified index.
    /// Creates a copy of the page.
    /// </summary>
    /// <param name="index">Index at which to insert the page.</param>
    /// <param name="page">The page to insert.</param>
    public void Insert(int index, PdfPage page)
    {
        if (index < 0 || index > _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Insert index must be between 0 and {_pages.Count}");

        // Clone the page dictionary
        var newPageDict = ClonePageDictionary(page);

        // Set the parent to our Pages dictionary
        // Note: In a full implementation, we'd create a new indirect object reference
        // For now, we reference the parent directly
        newPageDict["Parent"] = _document.Catalog.GetReference("Pages");

        // Add to Kids array
        // Note: This simplified implementation adds directly to the array
        // A full implementation would create a proper indirect object
        _kidsArray.Insert(index, newPageDict);
        _pagesDict["Kids"] = _kidsArray;

        // Update Count
        _pagesDict.SetInt("Count", _pages.Count + 1);

        // Reload pages to get correct page numbers
        LoadPages();
    }

    /// <summary>
    /// Remove the page at the specified index.
    /// </summary>
    /// <param name="index">Index of the page to remove.</param>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Page index must be between 0 and {_pages.Count - 1}");

        if (_pages.Count <= 1)
            throw new InvalidOperationException("Cannot remove the last page from a document");

        // Remove from Kids array
        _kidsArray.RemoveAt(index);
        _pagesDict["Kids"] = _kidsArray;

        // Update Count
        _pagesDict.SetInt("Count", _pages.Count - 1);

        // Reload pages
        LoadPages();
    }

    /// <summary>
    /// Move a page from one position to another.
    /// </summary>
    /// <param name="fromIndex">Current index of the page.</param>
    /// <param name="toIndex">New index for the page.</param>
    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(fromIndex), $"From index must be between 0 and {_pages.Count - 1}");
        if (toIndex < 0 || toIndex >= _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(toIndex), $"To index must be between 0 and {_pages.Count - 1}");

        if (fromIndex == toIndex)
            return;

        // Get the page reference from Kids array
        var pageRef = _kidsArray[fromIndex];

        // Remove from current position
        _kidsArray.RemoveAt(fromIndex);

        // Insert at new position (adjust if moving forward)
        _kidsArray.Insert(toIndex, pageRef);

        _pagesDict["Kids"] = _kidsArray;

        // Reload pages
        LoadPages();
    }

    /// <summary>
    /// Clone a page dictionary for insertion into this document.
    /// </summary>
    private PdfDictionary ClonePageDictionary(PdfPage sourcePage)
    {
        var sourceDict = sourcePage.Dictionary;
        var newDict = new PdfDictionary();

        // Copy all entries except Parent (we'll set our own)
        foreach (var kvp in sourceDict)
        {
            if (kvp.Key.Value == "Parent")
                continue;

            // For now, do a shallow copy
            // A full implementation would deep-copy objects from other documents
            newDict[kvp.Key.Value] = kvp.Value;
        }

        // Ensure required entries
        newDict.SetName("Type", "Page");

        return newDict;
    }

    /// <summary>
    /// Get the pages dictionary (for internal use).
    /// </summary>
    internal PdfDictionary PagesDictionary => _pagesDict;

    /// <inheritdoc />
    public IEnumerator<PdfPage> GetEnumerator() => _pages.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
