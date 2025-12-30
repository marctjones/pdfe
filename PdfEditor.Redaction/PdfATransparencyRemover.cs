using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfEditor.Redaction;

/// <summary>
/// Removes transparency features from PDFs to ensure PDF/A-1 compliance.
/// PDF/A-1 (ISO 19005-1) forbids transparency entirely.
/// </summary>
public class PdfATransparencyRemover
{
    private readonly ILogger<PdfATransparencyRemover> _logger;

    public PdfATransparencyRemover() : this(NullLogger<PdfATransparencyRemover>.Instance) { }

    public PdfATransparencyRemover(ILogger<PdfATransparencyRemover> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Remove transparency from the document for PDF/A-1 compliance.
    /// This modifies the document in place.
    /// </summary>
    /// <param name="document">The PDF document to modify.</param>
    /// <returns>Number of transparency features removed.</returns>
    public int RemoveTransparency(PdfDocument document)
    {
        int removed = 0;

        foreach (var page in document.Pages)
        {
            removed += RemoveTransparencyFromPage(page);
        }

        _logger.LogDebug("Removed {Count} transparency features from document", removed);
        return removed;
    }

    /// <summary>
    /// Remove transparency from a single page.
    /// </summary>
    public int RemoveTransparencyFromPage(PdfPage page)
    {
        int removed = 0;

        // Remove /Group from page dictionary (transparency group)
        if (page.Elements.ContainsKey("/Group"))
        {
            var group = page.Elements.GetDictionary("/Group");
            if (group != null)
            {
                var sValue = group.Elements.GetName("/S");
                if (sValue == "/Transparency")
                {
                    page.Elements.Remove("/Group");
                    _logger.LogDebug("Removed transparency group from page");
                    removed++;
                }
            }
        }

        // Remove transparency from Form XObjects in page resources
        removed += RemoveTransparencyFromResources(page);

        return removed;
    }

    /// <summary>
    /// Remove transparency from page resources (XObjects, ExtGState, etc.)
    /// </summary>
    private int RemoveTransparencyFromResources(PdfPage page)
    {
        int removed = 0;

        try
        {
            var resources = page.Elements.GetDictionary("/Resources");
            if (resources == null)
                return 0;

            // Handle XObjects
            var xobjects = resources.Elements.GetDictionary("/XObject");
            if (xobjects != null)
            {
                removed += RemoveTransparencyFromXObjects(xobjects);
            }

            // Handle ExtGState (graphics state with transparency settings)
            var extGState = resources.Elements.GetDictionary("/ExtGState");
            if (extGState != null)
            {
                removed += RemoveTransparencyFromExtGState(extGState);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing page resources");
        }

        return removed;
    }

    /// <summary>
    /// Remove transparency groups from XObjects.
    /// </summary>
    private int RemoveTransparencyFromXObjects(PdfDictionary xobjects)
    {
        int removed = 0;

        foreach (var key in xobjects.Elements.Keys.ToList())
        {
            try
            {
                var xobject = xobjects.Elements.GetDictionary(key);
                if (xobject == null)
                {
                    // Try to resolve reference
                    var reference = xobjects.Elements.GetReference(key);
                    if (reference?.Value is PdfDictionary refDict)
                    {
                        xobject = refDict;
                    }
                }

                if (xobject != null)
                {
                    // Check if it's a Form XObject
                    var subtype = xobject.Elements.GetName("/Subtype");
                    if (subtype == "/Form")
                    {
                        // Remove transparency group from Form XObject
                        if (xobject.Elements.ContainsKey("/Group"))
                        {
                            var group = xobject.Elements.GetDictionary("/Group");
                            if (group != null)
                            {
                                var sValue = group.Elements.GetName("/S");
                                if (sValue == "/Transparency")
                                {
                                    xobject.Elements.Remove("/Group");
                                    _logger.LogDebug("Removed transparency group from Form XObject {Key}", key);
                                    removed++;
                                }
                            }
                        }

                        // Recursively handle nested resources
                        var nestedResources = xobject.Elements.GetDictionary("/Resources");
                        if (nestedResources != null)
                        {
                            var nestedXObjects = nestedResources.Elements.GetDictionary("/XObject");
                            if (nestedXObjects != null)
                            {
                                removed += RemoveTransparencyFromXObjects(nestedXObjects);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error processing XObject {Key}", key);
            }
        }

        return removed;
    }

    /// <summary>
    /// Remove transparency settings from ExtGState dictionaries.
    /// PDF/A-1 forbids: CA, ca (alpha), SMask (soft mask), BM (blend mode) other than Normal
    /// </summary>
    private int RemoveTransparencyFromExtGState(PdfDictionary extGState)
    {
        int removed = 0;

        foreach (var key in extGState.Elements.Keys.ToList())
        {
            try
            {
                var gsDict = extGState.Elements.GetDictionary(key);
                if (gsDict == null)
                {
                    var reference = extGState.Elements.GetReference(key);
                    if (reference?.Value is PdfDictionary refDict)
                    {
                        gsDict = refDict;
                    }
                }

                if (gsDict != null)
                {
                    // Remove CA (stroke alpha)
                    if (gsDict.Elements.ContainsKey("/CA"))
                    {
                        var ca = gsDict.Elements.GetReal("/CA");
                        if (ca < 1.0)
                        {
                            gsDict.Elements.SetReal("/CA", 1.0);
                            _logger.LogDebug("Set stroke alpha CA to 1.0 in ExtGState {Key}", key);
                            removed++;
                        }
                    }

                    // Remove ca (fill alpha)
                    if (gsDict.Elements.ContainsKey("/ca"))
                    {
                        var ca = gsDict.Elements.GetReal("/ca");
                        if (ca < 1.0)
                        {
                            gsDict.Elements.SetReal("/ca", 1.0);
                            _logger.LogDebug("Set fill alpha ca to 1.0 in ExtGState {Key}", key);
                            removed++;
                        }
                    }

                    // Remove SMask (soft mask)
                    if (gsDict.Elements.ContainsKey("/SMask"))
                    {
                        var smask = gsDict.Elements.GetName("/SMask");
                        if (smask != "/None")
                        {
                            gsDict.Elements.SetName("/SMask", "/None");
                            _logger.LogDebug("Set SMask to /None in ExtGState {Key}", key);
                            removed++;
                        }
                    }

                    // Set BM (blend mode) to Normal
                    if (gsDict.Elements.ContainsKey("/BM"))
                    {
                        var bm = gsDict.Elements.GetName("/BM");
                        if (bm != "/Normal")
                        {
                            gsDict.Elements.SetName("/BM", "/Normal");
                            _logger.LogDebug("Set blend mode BM to /Normal in ExtGState {Key}", key);
                            removed++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error processing ExtGState {Key}", key);
            }
        }

        return removed;
    }

    /// <summary>
    /// Check if a document has transparency features.
    /// </summary>
    public bool HasTransparency(PdfDocument document)
    {
        foreach (var page in document.Pages)
        {
            if (PageHasTransparency(page))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if a page has transparency features.
    /// </summary>
    public bool PageHasTransparency(PdfPage page)
    {
        // Check page-level transparency group
        if (page.Elements.ContainsKey("/Group"))
        {
            var group = page.Elements.GetDictionary("/Group");
            if (group?.Elements.GetName("/S") == "/Transparency")
                return true;
        }

        // Check resources for transparency
        try
        {
            var resources = page.Elements.GetDictionary("/Resources");
            if (resources != null)
            {
                // Check XObjects
                var xobjects = resources.Elements.GetDictionary("/XObject");
                if (xobjects != null)
                {
                    foreach (var key in xobjects.Elements.Keys)
                    {
                        var xobj = xobjects.Elements.GetDictionary(key) ??
                                   (xobjects.Elements.GetReference(key)?.Value as PdfDictionary);
                        if (xobj?.Elements.ContainsKey("/Group") == true)
                        {
                            var group = xobj.Elements.GetDictionary("/Group");
                            if (group?.Elements.GetName("/S") == "/Transparency")
                                return true;
                        }
                    }
                }

                // Check ExtGState for transparency
                var extGState = resources.Elements.GetDictionary("/ExtGState");
                if (extGState != null)
                {
                    foreach (var key in extGState.Elements.Keys)
                    {
                        var gs = extGState.Elements.GetDictionary(key) ??
                                 (extGState.Elements.GetReference(key)?.Value as PdfDictionary);
                        if (gs != null)
                        {
                            // Check for alpha < 1
                            if (gs.Elements.ContainsKey("/CA") && gs.Elements.GetReal("/CA") < 1.0)
                                return true;
                            if (gs.Elements.ContainsKey("/ca") && gs.Elements.GetReal("/ca") < 1.0)
                                return true;
                            // Check for soft mask
                            if (gs.Elements.ContainsKey("/SMask") && gs.Elements.GetName("/SMask") != "/None")
                                return true;
                            // Check for blend mode
                            if (gs.Elements.ContainsKey("/BM") && gs.Elements.GetName("/BM") != "/Normal")
                                return true;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors in transparency detection
        }

        return false;
    }
}
