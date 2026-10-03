using MaltasGarage.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace MaltasGarage.Infrastructure.Services;

public class LocalImageService : IImageService
{
    private readonly LocalUploadStorage _storage;
    private readonly ILogger<LocalImageService> _logger;

    public LocalImageService(LocalUploadStorage storage, ILogger<LocalImageService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<ImageUploadResult> UploadAsync(Stream stream, string fileName, string folder)
    {
        var invalid = ImageProcessor.Validate(stream, fileName);
        if (invalid != null)
            return new ImageUploadResult { Success = false, Error = invalid };

        try
        {
            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            var baseName = $"{uniqueId}.jpg";
            var thumbnailName = $"{uniqueId}_thumb.jpg";

            var uploadDir = _storage.FolderPath(folder);
            Directory.CreateDirectory(uploadDir);

            await using (var main = File.Create(Path.Combine(uploadDir, baseName)))
            await using (var thumb = File.Create(Path.Combine(uploadDir, thumbnailName)))
                await ImageProcessor.ProcessAsync(stream, main, thumb);

            return new ImageUploadResult
            {
                Success = true,
                Url = $"{LocalUploadStorage.RequestPath}/{folder}/{baseName}",
                ThumbnailUrl = $"{LocalUploadStorage.RequestPath}/{folder}/{thumbnailName}"
            };
        }
        catch (Exception ex)
        {
            // The details are for the log; the user only needs to know to try another file
            _logger.LogError(ex, "Image upload failed for file {FileName}", fileName);
            return new ImageUploadResult { Success = false, Error = "The image could not be processed. Please try another file." };
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

            File.Delete(path);                                  // no error when it is already gone
            File.Delete(ImageProcessor.ThumbnailOf(path));
        }
        catch (Exception ex)
        {
            // A file left behind is not worth failing the request for, but it is worth knowing about
            _logger.LogWarning(ex, "Could not delete image {Url}", url);
        }

        return Task.CompletedTask;
    }
}
