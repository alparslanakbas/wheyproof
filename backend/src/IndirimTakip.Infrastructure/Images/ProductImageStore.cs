using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace IndirimTakip.Infrastructure.Images;

/// <summary>
/// Downloads product images to our own server and resizes them.
/// </summary>
/// <remarks>
/// <b>WHY IT WAS NEEDED.</b> Images were hotlinked straight from each source's
/// CDN. Measured on the Turkish site: one sample per source averaged
/// <b>280 kB</b>, the largest <b>2.35 MB</b>; a 24-product list page downloaded
/// about 6.7 MB of images on its own. Hotlinks can also break silently: an
/// unreachable host does NOT fire <c>onerror</c>, the request just hangs and the
/// fallback image never kicks in.
///
/// <b>WHY 400 PIXELS.</b> The list card box is 78-92 px, a featured product
/// 120 px. A 2x screen needs at most ~240 px; 400 px leaves room for the product
/// page and a card that may grow later. A smaller source image is NOT upscaled:
/// upscaling adds bytes, not detail.
///
/// <b>THE FILE NAME IS A HASH OF THE SOURCE URL.</b> Not the product id: a source
/// can use the same image for several products (common at retailers), and when
/// the URL changes the old file is invalidated automatically. The extension is
/// always .webp, since there is one output format.
/// </remarks>
public sealed class ProductImageStore(
    IHttpClientFactory httpClientFactory,
    ProductImageOptions options,
    ILogger<ProductImageStore> logger)
{
    public const string HttpClientName = "product-images";

    /// <summary>Maximum pixels of the long edge.</summary>
    public const int MaxEdge = 400;

    /// <summary>
    /// Local file name for a source URL. A pure function: the same URL always
    /// gives the same name.
    /// </summary>
    public static string FileName(string sourceUrl)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl.Trim()));
        return Convert.ToHexStringLower(hash.AsSpan(0, 12)) + ".webp";
    }

    /// <summary>Public URL of the local file; null without a file name.</summary>
    public static string? PublicUrl(string? fileName, string baseUrl) =>
        string.IsNullOrEmpty(fileName) ? null : $"{baseUrl.TrimEnd('/')}/{fileName}";

    /// <summary>
    /// Downloads, resizes and writes the image to disk; returns the file name on
    /// success, otherwise null.
    /// </summary>
    public async Task<string?> DownloadAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        var fileName = FileName(sourceUrl);
        var target = Path.Combine(options.StoragePath, fileName);

        // Not downloaded again if it already exists: a run cut off and restarted
        // shouldn't redo the same work.
        if (File.Exists(target))
            return fileName;

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(
                sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            // If the server declares a size above the cap, the body is never
            // downloaded. If it doesn't, the read limit below protects us.
            if (response.Content.Headers.ContentLength > options.MaxBytes)
                return null;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            await CopyWithLimitAsync(source, buffer, options.MaxBytes, cancellationToken);
            buffer.Position = 0;

            var webp = await ResizeAsync(buffer, options.Quality, cancellationToken);

            Directory.CreateDirectory(options.StoragePath);

            // TEMPORARY FILE FIRST, THEN MOVE. Writing straight to the target, a
            // request arriving mid-write would see a partial file, and that broken
            // image would end up in the browser cache.
            var temporary = target + ".tmp";
            await File.WriteAllBytesAsync(temporary, webp, cancellationToken);
            File.Move(temporary, target, overwrite: true);
            return fileName;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One image failing must not take the run down: the source URL may be
            // dead or the format broken. That product stays without a local copy
            // and keeps being shown with its SOURCE URL.
            logger.LogDebug(ex, "Could not download product image: {Url}", sourceUrl);
            return null;
        }
    }

    /// <summary>
    /// Resizes the image so its long edge is at most <see cref="MaxEdge"/> and
    /// converts it to WebP. Kept apart from network and disk so the actual
    /// conversion can be tested.
    /// </summary>
    internal static async Task<byte[]> ResizeAsync(Stream input, int quality, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync(input, cancellationToken);

        // SMALLER IMAGES AREN'T UPSCALED: Max mode only shrinks an edge above the
        // limit, so an already small image stays as is. Upscaling adds bytes, not
        // detail.
        if (image.Width > MaxEdge || image.Height > MaxEdge)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxEdge, MaxEdge),
            }));
        }

        using var output = new MemoryStream();
        await image.SaveAsync(output, new WebpEncoder { Quality = quality }, cancellationToken);
        return output.ToArray();
    }

    /// <summary>Deletes files no product uses anymore.</summary>
    public int DeleteUnused(IReadOnlySet<string> inUse)
    {
        if (!Directory.Exists(options.StoragePath))
            return 0;

        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(options.StoragePath, "*.webp"))
        {
            if (inUse.Contains(Path.GetFileName(path)))
                continue;

            try
            {
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete leftover image: {Path}", path);
            }
        }

        return deleted;
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream target, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException("Image exceeded the size limit.");

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}

public sealed class ProductImageOptions
{
    /// <summary>Directory the images are written to (path inside the container).</summary>
    public string StoragePath { get; set; } = "/app/product-images";

    /// <summary>Public URL prefix of the local images; must match Program.cs (/api/images).</summary>
    public string PublicBaseUrl { get; set; } = "https://api.wheyproof.com/api/images";

    public bool Enabled { get; set; } = true;

    /// <summary>Maximum images downloaded per run.</summary>
    public int MaxPerRun { get; set; } = 150;

    public int IntervalMinutes { get; set; } = 10;

    public int Quality { get; set; } = 78;

    /// <summary>Largest source file downloaded (bytes).</summary>
    public long MaxBytes { get; set; } = 12 * 1024 * 1024;
}
