using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace MaltasGarage.Infrastructure.Services;

/// <summary>
/// The part of an upload both image stores share: check the file, then produce the full-size
/// JPEG and the thumbnail. Photos from phones carry EXIF with the GPS position of the seller's
/// home; it is applied (orientation) and then dropped, along with every other metadata block.
/// </summary>
public static class ImageProcessor
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const int MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxDimension = 1920;
    private const int ThumbnailWidth = 400;
    private const int ThumbnailHeight = 300;

    /// <summary>A message for the user when the file cannot be accepted, otherwise null.</summary>
    public static string? Validate(Stream stream, string fileName)
    {
        if (!AllowedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
            return "Invalid file type. Allowed: JPG, PNG, WEBP";

        return stream.Length > MaxFileSizeBytes ? "File too large. Maximum size: 5MB" : null;
    }

    /// <summary>Writes the full-size image and the thumbnail as JPEG, without any metadata.</summary>
    public static async Task ProcessAsync(Stream source, Stream main, Stream thumbnail)
    {
        source.Position = 0;
        using var image = await Image.LoadAsync(source);

        // Turn the pixels the way EXIF says before the EXIF goes, or portrait photos end up sideways
        image.Mutate(x => x.AutoOrient());
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.IccProfile = null;

        if (image.Width > MaxDimension || image.Height > MaxDimension)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(MaxDimension, MaxDimension),
                Mode = ResizeMode.Max
            }));
        }

        await image.SaveAsJpegAsync(main);

        // Thumbnail: 400x300 crop from center
        using var thumb = image.Clone(x => x.Resize(new ResizeOptions
        {
            Size = new Size(ThumbnailWidth, ThumbnailHeight),
            Mode = ResizeMode.Crop
        }));
        await thumb.SaveAsJpegAsync(thumbnail);
    }

    /// <summary>The thumbnail name stored next to an image: photo.jpg -> photo_thumb.jpg.</summary>
    public static string ThumbnailOf(string path)
    {
        var ext = Path.GetExtension(path);
        return path[..^ext.Length] + "_thumb" + ext;
    }
}
