using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;

namespace PdfEditor.ViewModels;

/// <summary>
/// ViewModel for the Save Redacted Version dialog.
/// Prompts user to choose filename for redacted PDF.
/// </summary>
public class SaveRedactedVersionDialogViewModel : ReactiveObject
{
    private string _saveFilePath;
    private int _pendingCount;

    /// <summary>
    /// The path where the redacted PDF will be saved
    /// </summary>
    public string SaveFilePath
    {
        get => _saveFilePath;
        set => this.RaiseAndSetIfChanged(ref _saveFilePath, value);
    }

    /// <summary>
    /// Number of pending redactions to be applied
    /// </summary>
    public int PendingCount
    {
        get => _pendingCount;
        set => this.RaiseAndSetIfChanged(ref _pendingCount, value);
    }

    /// <summary>
    /// Text showing count of redactions (e.g., "3 areas will be redacted")
    /// </summary>
    public string PendingCountText => PendingCount == 1
        ? "1 area will be redacted"
        : $"{PendingCount} areas will be redacted";

    /// <summary>
    /// Command to open file browser for save location
    /// </summary>
    public ReactiveCommand<Unit, string?> BrowseCommand { get; }

    /// <summary>
    /// Command to confirm and save
    /// </summary>
    public ReactiveCommand<Unit, string?> SaveCommand { get; }

    /// <summary>
    /// Command to cancel
    /// </summary>
    public ReactiveCommand<Unit, string?> CancelCommand { get; }

    public SaveRedactedVersionDialogViewModel(string suggestedFilePath, int pendingCount)
    {
        _saveFilePath = suggestedFilePath;
        _pendingCount = pendingCount;

        // Browse command - will be handled by view to open file picker
        BrowseCommand = ReactiveCommand.Create<string?>(() => null);

        // Save command - enabled only when path is valid
        var canSave = this.WhenAnyValue(
            x => x.SaveFilePath,
            path => !string.IsNullOrWhiteSpace(path) && IsValidPath(path));

        SaveCommand = ReactiveCommand.Create<string?>(() => SaveFilePath, canSave);

        // Cancel command - always enabled
        CancelCommand = ReactiveCommand.Create<string?>(() => null);
    }

    private bool IsValidPath(string path)
    {
        try
        {
            // Check if directory exists
            var dir = Path.GetDirectoryName(path);
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir);
        }
        catch
        {
            return false;
        }
    }
}
