using Avalonia.Media.Imaging;
using ReactiveUI;

namespace PdfEditor.Models;

public class PageThumbnail : ReactiveObject
{
    private Bitmap? _thumbnailImage;

    public int PageNumber { get; set; }
    public int PageIndex { get; set; }

    public Bitmap? ThumbnailImage
    {
        get => _thumbnailImage;
        set => this.RaiseAndSetIfChanged(ref _thumbnailImage, value);
    }
}
