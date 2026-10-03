using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Infrastructure.Services;

public class AzureBlobImageService : IImageService
{
    private static readonly BlobHttpHeaders JpegHeaders = new() { ContentType = "image/jpeg", CacheControl = "public, max-age=31536000" };

    // The service is scoped; the container check runs once per process, not once per request
    private static Task? _containerReady;
    private static readonly object ContainerLock = new();

    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureBlobImageService> _logger;

    public AzureBlobImageService(IOptions<StorageSettings> settings, ILogger<AzureBlobImageService> logger)
    {
        _logger = logger;
        var storage = settings.Value;
        _container = new BlobContainerClient(storage.AzureBlobConnectionString, storage.AzureBlobContainer);
    }

    private Task EnsureContainerAsync()
    {
        lock (ContainerLock)
        {
            // A failed check is retried on the next upload instead of being cached
            if (_containerReady == null || _containerReady.IsFaulted)
                _containerReady = _container.CreateIfNotExistsAsync(PublicAccessType.None);
            return _containerReady;
        }
    }

    public async Task<ImageUploadResult> UploadAsync(Stream stream, string fileName, string folder)
    {
        var invalid = ImageProcessor.Validate(stream, fileName);
        if (invalid != null)
            return new ImageUploadResult { Success = false, Error = invalid };

        try
        {
            await EnsureContainerAsync();

            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            var baseName = $"{folder}/{uniqueId}.jpg";
            var thumbnailName = $"{folder}/{uniqueId}_thumb.jpg";

            using var mainStream = new MemoryStream();
            using var thumbStream = new MemoryStream();
            await ImageProcessor.ProcessAsync(stream, mainStream, thumbStream);

            mainStream.Position = 0;
            var mainBlob = _container.GetBlobClient(baseName);
            await mainBlob.UploadAsync(mainStream, JpegHeaders);

            thumbStream.Position = 0;
            var thumbBlob = _container.GetBlobClient(thumbnailName);
            await thumbBlob.UploadAsync(thumbStream, JpegHeaders);

            return new ImageUploadResult
            {
                Success = true,
                Url = mainBlob.Uri.ToString(),
                ThumbnailUrl = thumbBlob.Uri.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AzureBlobImageService.UploadAsync failed for file {FileName}", fileName);
            return new ImageUploadResult { Success = false, Error = "The image could not be processed. Please try another file." };
        }
    }

    public async Task DeleteAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            var uri = new Uri(url);
            var blobName = uri.AbsolutePath.TrimStart('/').Substring(_container.Name.Length + 1);
            await _container.GetBlobClient(blobName).DeleteIfExistsAsync();
            await _container.GetBlobClient(ImageProcessor.ThumbnailOf(blobName)).DeleteIfExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AzureBlobImageService.DeleteAsync failed for URL {Url}", url);
        }
    }
}
