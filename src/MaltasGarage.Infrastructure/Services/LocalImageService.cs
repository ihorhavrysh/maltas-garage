using MaltasGarage.Application.Common.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace MaltasGarage.Infrastructure.Services;

public class LocalImageService : IImageService
{
    private readonly LocalUploadStorage _storage;
    private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const int MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxDimension = 1920;
    private const int ThumbnailWidth = 400;
    private const int ThumbnailHeight = 300;

    public LocalImageService(LocalUploadStorage storage)
    {
        _storage = storage;
    }

    public async Task<ImageUploadResult> UploadAsync(Stream stream, string fileName, string folder)
    {
        try
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!_allowedExtensions.Contains(extension))
            {
                return new ImageUploadResult
                {
                    Success = false,
                    Error = "Invalid file type. Allowed: JPG, PNG, WEBP"
                };
            }

            if (stream.Length > MaxFileSizeBytes)
            {
                return new ImageUploadResult
                {
                    Success = false,
                    Error = "File too large. Maximum size: 5MB"
                };
            }

            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            var baseName = $"{uniqueId}.jpg";
            var thumbnailName = $"{uniqueId}_thumb.jpg";

            var uploadDir = _storage.FolderPath(folder);
            Directory.CreateDirectory(uploadDir);

            stream.Position = 0;
            using var image = await Image.LoadAsync(stream);

            // Resize if too large, keep aspect ratio
            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxDimension, MaxDimension),
                    Mode = ResizeMode.Max
                }));
            }

            await image.SaveAsJpegAsync(Path.Combine(uploadDir, baseName));

            // Thumbnail: 400x300 crop from center
            using var thumbnail = image.Clone(x => x.Resize(new ResizeOptions
            {
                Size = new Size(ThumbnailWidth, ThumbnailHeight),
                Mode = ResizeMode.Crop
            }));
            await thumbnail.SaveAsJpegAsync(Path.Combine(uploadDir, thumbnailName));

            return new ImageUploadResult
            {
                Success = true,
                Url = $"{LocalUploadStorage.RequestPath}/{folder}/{baseName}",
                ThumbnailUrl = $"{LocalUploadStorage.RequestPath}/{folder}/{thumbnailName}"
            };
        }
        catch (Exception ex)
        {
            return new ImageUploadResult
            {
                Success = false,
                Error = $"Upload failed: {ex.Message}"
            };
        }
    }

    public Task DeleteAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
            return Task.CompletedTask;

        try
        {
            var path = _storage.PathFromUrl(url);
            if (path == null)
                return Task.CompletedTask;
            if (File.Exists(path))
                File.Delete(path);

            // Delete thumbnail too (same name with _thumb suffix)
            var ext = Path.GetExtension(path);
            var thumbPath = path.Replace(ext, $"_thumb{ext}");
            if (File.Exists(thumbPath))
                File.Delete(thumbPath);
        }
        catch
        {
            // log but don't throw
        }

        return Task.CompletedTask;
    }
}
