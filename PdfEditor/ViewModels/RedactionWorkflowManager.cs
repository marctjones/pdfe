using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using PdfEditor.Models;
using ReactiveUI;

namespace PdfEditor.ViewModels;

/// <summary>
/// Manages the mark-then-apply redaction workflow.
/// Tracks pending and applied redactions.
/// </summary>
public class RedactionWorkflowManager : ReactiveObject
{
    private readonly ObservableCollection<PendingRedaction> _pending = new();
    private readonly ObservableCollection<PendingRedaction> _applied = new();

    /// <summary>
    /// Redactions that have been marked but not yet applied
    /// </summary>
    public ReadOnlyObservableCollection<PendingRedaction> PendingRedactions { get; }

    /// <summary>
    /// Redactions that have been applied and saved
    /// </summary>
    public ReadOnlyObservableCollection<PendingRedaction> AppliedRedactions { get; }

    /// <summary>
    /// Number of pending redactions
    /// </summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Number of applied redactions
    /// </summary>
    public int AppliedCount => _applied.Count;

    public RedactionWorkflowManager()
    {
        PendingRedactions = new ReadOnlyObservableCollection<PendingRedaction>(_pending);
        AppliedRedactions = new ReadOnlyObservableCollection<PendingRedaction>(_applied);
    }

    /// <summary>
    /// Mark an area for redaction (adds to pending list)
    /// </summary>
    public void MarkArea(int pageNumber, Rect area, string previewText)
    {
        var pending = new PendingRedaction
        {
            PageNumber = pageNumber,
            Area = area,
            PreviewText = previewText,
            MarkedTime = DateTime.Now
        };

        _pending.Add(pending);
        this.RaisePropertyChanged(nameof(PendingCount));
        this.RaisePropertyChanged(nameof(PendingRedactions)); // Force UI update
    }

    /// <summary>
    /// Remove a pending redaction by ID
    /// </summary>
    public bool RemovePending(Guid id)
    {
        var item = _pending.FirstOrDefault(p => p.Id == id);
        if (item != null)
        {
            _pending.Remove(item);
            this.RaisePropertyChanged(nameof(PendingCount));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clear all pending redactions
    /// </summary>
    public void ClearPending()
    {
        _pending.Clear();
        this.RaisePropertyChanged(nameof(PendingCount));
    }

    /// <summary>
    /// Move all pending redactions to applied (after successful save)
    /// </summary>
    public void MoveToApplied()
    {
        foreach (var pending in _pending)
        {
            _applied.Add(pending);
        }

        _pending.Clear();
        this.RaisePropertyChanged(nameof(PendingCount));
        this.RaisePropertyChanged(nameof(AppliedCount));
    }

    /// <summary>
    /// Get pending redactions for a specific page
    /// </summary>
    public IEnumerable<PendingRedaction> GetPendingForPage(int pageNumber)
    {
        return _pending.Where(p => p.PageNumber == pageNumber);
    }

    /// <summary>
    /// Get applied redactions for a specific page
    /// </summary>
    public IEnumerable<PendingRedaction> GetAppliedForPage(int pageNumber)
    {
        return _applied.Where(a => a.PageNumber == pageNumber);
    }

    /// <summary>
    /// Clear all state (e.g., when closing document)
    /// </summary>
    public void Reset()
    {
        _pending.Clear();
        _applied.Clear();
        this.RaisePropertyChanged(nameof(PendingCount));
        this.RaisePropertyChanged(nameof(AppliedCount));
    }
}
