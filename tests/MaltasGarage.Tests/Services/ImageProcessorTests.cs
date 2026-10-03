using MaltasGarage.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace MaltasGarage.Tests.Services;

public class ImageProcessorTests
{
    [Fact]
    public async Task Process_DropsExifIncludingGps()
    {
        using var source = new MemoryStream();
        using (var photo = new Image<Rgba32>(800, 600))
        {
            photo.Metadata.ExifProfile = new ExifProfile();
            photo.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
            photo.Metadata.ExifProfile.SetValue(ExifTag.Make, "PhoneMaker");
            await photo.SaveAsJpegAsync(source);
        }

        using var main = new MemoryStream();
        using var thumb = new MemoryStream();
        await ImageProcessor.ProcessAsync(source, main, thumb);

        main.Position = 0;
        using var stored = await Image.LoadAsync(main);
        Assert.Null(stored.Metadata.ExifProfile);

        thumb.Position = 0;
        using var storedThumb = await Image.LoadAsync(thumb);
        Assert.Equal((400, 300), (storedThumb.Width, storedThumb.Height));
    }

    [Theory]
    [InlineData("photo.gif")]
    [InlineData("photo.exe")]
    public void Validate_RejectsOtherFileTypes(string fileName)
    {
        using var stream = new MemoryStream(new byte[10]);
        Assert.NotNull(ImageProcessor.Validate(stream, fileName));
    }

    [Fact]
    public void ThumbnailOf_InsertsSuffixBeforeExtension()
    {
        Assert.Equal("listings/ab12_thumb.jpg", ImageProcessor.ThumbnailOf("listings/ab12.jpg"));
    }
}
