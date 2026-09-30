namespace MaltasGarage.Application.Common.Interfaces;

public interface IImageService
{
    Task<ImageUploadResult> UploadAsync(Stream stream, string fileName, string folder);
    Task DeleteAsync(string url);
}

public class ImageUploadResult
{
    public bool Success { get; set; }
    public string? Url { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Error { get; set; }
}
