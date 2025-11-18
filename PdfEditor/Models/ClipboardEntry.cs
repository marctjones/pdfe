using System;
using ReactiveUI;

namespace PdfEditor.Models;

public class ClipboardEntry : ReactiveObject
{
    private string _text = string.Empty;
    private DateTime _timestamp;
    private int _pageNumber;

    public string Text
    {
        get => _text;
        set => this.RaiseAndSetIfChanged(ref _text, value);
    }

    public DateTime Timestamp
    {
        get => _timestamp;
        set => this.RaiseAndSetIfChanged(ref _timestamp, value);
    }

    public int PageNumber
    {
        get => _pageNumber;
        set => this.RaiseAndSetIfChanged(ref _pageNumber, value);
    }
}
