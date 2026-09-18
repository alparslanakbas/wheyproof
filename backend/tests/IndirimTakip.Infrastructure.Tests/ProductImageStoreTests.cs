using IndirimTakip.Infrastructure.Images;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// File name generation, URL resolution and the resize step. The download itself
/// needs network and disk, so it was measured against production.
/// </summary>
public class ProductImageStoreTests
{
    /// <summary>
    /// The name is a hash of the source URL, so the SAME URL must always give the
    /// same name; the "already downloaded?" check depends on it.
    /// </summary>
    [Fact]
    public void Same_url_gives_the_same_file_name()
    {
        var a = ProductImageStore.FileName("https://example.com/cdn/shop/files/whey-protein.jpg");
        var b = ProductImageStore.FileName("https://example.com/cdn/shop/files/whey-protein.jpg");

        Assert.Equal(a, b);
        Assert.EndsWith(".webp", a);
    }

    [Fact]
    public void Different_url_gives_a_different_file_name()
    {
        var a = ProductImageStore.FileName("https://example.com/a.jpg");
        var b = ProductImageStore.FileName("https://example.com/b.jpg");

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// The name must fit the column limit (64) comfortably; otherwise the value is
    /// truncated and the file can never be found again.
    /// </summary>
    [Fact]
    public void File_name_fits_the_column_limit()
    {
        var name = ProductImageStore.FileName("https://example.com/" + new string('u', 2000) + ".jpg");

        Assert.True(name.Length <= 64, $"file name is {name.Length} characters");
    }

    [Theory]
    [InlineData("https://api.wheyproof.com/api/images")]
    // A trailing slash must not produce a double slash.
    [InlineData("https://api.wheyproof.com/api/images/")]
    public void Public_url_is_joined_correctly(string baseUrl)
    {
        var url = ProductImageStore.PublicUrl("abc123.webp", baseUrl);

        Assert.Equal("https://api.wheyproof.com/api/images/abc123.webp", url);
    }

    /// <summary>
    /// Without a local copy it must return null: the caller then falls back to the
    /// SOURCE URL, so the site works as before until the download finishes.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_a_local_copy_returns_null(string? local)
    {
        Assert.Null(ProductImageStore.PublicUrl(local, "https://example.com/images"));
    }

    /// <summary>
    /// THE ACTUAL JOB: a 1200 px image must come down to 400 px and be written as
    /// WebP. The library path (decode, resize, WebP encode) must REALLY run at least
    /// once before a deploy.
    /// </summary>
    [Fact]
    public async Task Large_image_becomes_400_pixel_webp()
    {
        using var input = FakeImage(1200, 900);

        var webp = await ProductImageStore.ResizeAsync(input, 78, CancellationToken.None);

        Assert.NotEmpty(webp);
        // WebP container: "RIFF" + size + "WEBP".
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(webp, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(webp, 8, 4));

        using var result = SixLabors.ImageSharp.Image.Load(webp);
        Assert.Equal(ProductImageStore.MaxEdge, result.Width);
        Assert.Equal(300, result.Height); // 1200x900 -> 400x300, aspect ratio kept
    }

    /// <summary>
    /// An image below the limit must NOT be upscaled: upscaling adds bytes, not
    /// detail.
    /// </summary>
    [Fact]
    public async Task Small_image_is_not_upscaled()
    {
        using var input = FakeImage(150, 150);

        var webp = await ProductImageStore.ResizeAsync(input, 78, CancellationToken.None);

        using var result = SixLabors.ImageSharp.Image.Load(webp);
        Assert.Equal(150, result.Width);
        Assert.Equal(150, result.Height);
    }

    private static MemoryStream FakeImage(int width, int height)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        image.Mutate(x => x.BackgroundColor(SixLabors.ImageSharp.Color.CornflowerBlue));

        var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    // Bare Performance Nutrition's 15.6 MB originals were above MaxBytes and never
    // stored; Shopify's CDN returns the same image at 800 px as 2.0 MB.
    [Theory]
    [InlineData("https://cdn.shopify.com/s/files/1/1103/4864/files/CREATINE.png?v=1786117372",
        "https://cdn.shopify.com/s/files/1/1103/4864/files/CREATINE.png?v=1786117372&width=800")]
    [InlineData("https://cdn.shopify.com/s/files/1/x/files/a.png",
        "https://cdn.shopify.com/s/files/1/x/files/a.png?width=800")]
    [InlineData("https://www.kaged.com/cdn/shop/files/whey.png?v=1",
        "https://www.kaged.com/cdn/shop/files/whey.png?v=1&width=800")]
    public void Shopify_images_are_fetched_resized(string source, string expected) =>
        Assert.Equal(expected, ProductImageStore.DownloadUrl(source));

    [Theory]
    // A width already chosen by the store is kept; other hosts are untouched.
    [InlineData("https://cdn.shopify.com/s/files/1/x/files/a.png?width=1200")]
    [InlineData("https://formnutrition.com/wp-content/uploads/protein.png")]
    [InlineData("not a url")]
    public void Other_images_are_fetched_as_published(string source) =>
        Assert.Equal(source, ProductImageStore.DownloadUrl(source));

    // A timeout means "this image failed", not "the app is stopping". HttpClient
    // throws it as TaskCanceledException; the old filter let it escape and the
    // background service left its loop (2026-09-18).
    [Fact]
    public async Task A_timeout_returns_null_instead_of_stopping_the_service()
    {
        var store = new ProductImageStore(
            new TimeoutFactory(),
            new ProductImageOptions { StoragePath = Path.Combine(Path.GetTempPath(), "img-" + Guid.NewGuid().ToString("N")) },
            NullLogger<ProductImageStore>.Instance);

        Assert.Null(await store.DownloadAsync("https://example.com/slow.jpg", CancellationToken.None));
    }

    [Fact]
    public async Task A_real_cancellation_still_propagates()
    {
        var store = new ProductImageStore(
            new TimeoutFactory(),
            new ProductImageOptions { StoragePath = Path.GetTempPath() },
            NullLogger<ProductImageStore>.Instance);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.DownloadAsync("https://example.com/cancel.jpg", cancel.Token));
    }

    private sealed class TimeoutFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new TimeoutHandler());
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
        }
    }
}
