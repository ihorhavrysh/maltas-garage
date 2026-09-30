using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace MaltasGarage.Infrastructure.Services;

public class AzureBlobImageService : IImageService
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureBlobImageService> _logger;
    private const int MaxDimension = 1920;
    private const int ThumbnailWidth = 400;
    private const int ThumbnailHeight = 300;
    private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const int MaxFileSizeBytes = 5 * 1024 * 1024;

    public AzureBlobImageService(IOptions<StorageSettings> settings, ILogger<AzureBlobImageService> logger)
    {
        _logger = logger;
        var storage = settings.Value;
        _container = new BlobContainerClient(storage.AzureBlobConnectionString, storage.AzureBlobContainer);
        try
        {
            // Create container if it doesn't exist. Use None; set public access in Azure Portal if needed.
            _container.CreateIfNotExists(PublicAccessType.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AzureBlobImageService: could not create container '{Container}' - it may already exist or permissions are insufficient",
                storage.AzureBlobContainer);
        }
        _logger.LogInformation("AzureBlobImageService initialised. Container URI: {Uri}", _container.Uri);
    }

    public async Task<ImageUploadResult> UploadAsync(Stream stream, string fileName, string folder)
    {
        try
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!_allowedExtensions.Contains(extension))
                return new ImageUploadResult { Success = false, Error = "Invalid file type. Allowed: JPG, PNG, WEBP" };

            if (stream.Length > MaxFileSizeBytes)
                return new ImageUploadResult { Success = false, Error = "File too large. Maximum size: 5MB" };

            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            var baseName = $"{folder}/{uniqueId}.jpg";
            var thumbnailName = $"{folder}/{uniqueId}_thumb.jpg";

            stream.Position = 0;
            using var image = await Image.LoadAsync(stream);

            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxDimension, MaxDimension),
                    Mode = ResizeMode.Max
                }));
            }

            using var mainStream = new MemoryStream();
            await image.SaveAsJpegAsync(mainStream);
            mainStream.Position = 0;
            var mainBlob = _container.GetBlobClient(baseName);
            await mainBlob.UploadAsync(mainStream, new BlobHttpHeaders { ContentType = "image/jpeg", CacheControl = "public, max-age=31536000" });

            using var thumbImage = image.Clone(x => x.Resize(new ResizeOptions
            {
                Size = new Size(ThumbnailWidth, ThumbnailHeight),
                Mode = ResizeMode.Crop
            }));
            using var thumbStream = new MemoryStream();
            await thumbImage.SaveAsJpegAsync(thumbStream);
            thumbStream.Position = 0;
            var thumbBlob = _container.GetBlobClient(thumbnailName);
            await thumbBlob.UploadAsync(thumbStream, new BlobHttpHeaders { ContentType = "image/jpeg", CacheControl = "public, max-age=31536000" });

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
            return new ImageUploadResult { Success = false, Error = $"Upload failed: {ex.Message}" };
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

            // Delete thumbnail too
            var thumbBlobName = blobName.Replace(".jpg", "_thumb.jpg");
            await _container.GetBlobClient(thumbBlobName).DeleteIfExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AzureBlobImageService.DeleteAsync failed for URL {Url}", url);
        }
    }
}
