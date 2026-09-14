using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace IndirimTakip.Infrastructure.NutritionLabels;

/// <summary>
/// Reads a label image with Tesseract OCR on the server: free, no outside API.
/// </summary>
/// <remarks>
/// <b>Measured before it was built (2026-09-14, 32 real labels):</b> every one
/// of the 6 readings that passed the calorie check matched the image exactly,
/// and the check rejected the 2 misreads ("2g" fat read as "29", "25g" protein
/// as "259"). Supplement Facts panels failed: the amount column was lost. So
/// this engine publishes calorie-checked Nutrition Facts panels only.
///
/// <b>Grayscale and a 2x upscale</b> before OCR: the panel is a small part of
/// a product photo, and its small print is what gets misread.
///
/// <b>Three page segmentation modes, first accepted wins.</b> Mode 3 (auto)
/// reads most panels; on two Orgain labels only mode 11 (sparse text) found
/// every value.
///
/// <b>One OCR thread.</b> The server also runs the Turkish site; Tesseract
/// would otherwise take every core for each image.
/// </remarks>
public sealed class TesseractLabelReader(HttpClient httpClient, NutritionLabelOptions options, ILogger<TesseractLabelReader> logger)
    : INutritionLabelReader
{
    public const string HttpClientName = "label-images";

    private const int DownloadWidth = 2048;
    private const int MaxOcrEdge = 4096;
    private static readonly int[] PageSegmentationModes = [3, 11, 6];
    private static readonly TimeSpan OcrTimeout = TimeSpan.FromSeconds(60);

    // Checked once per process: whether the binary is installed doesn't change
    // while the container runs.
    private static bool? installed;

    public string Engine => "tesseract";
    public bool RequiresCalorieCheck => true;
    public bool IsAvailable => installed ??= CheckInstalled(options.TesseractPath);

    public async Task<NutritionLabelReadResult> ReadAsync(string imageUrl, string? model, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            using var response = await httpClient.GetAsync(NutritionLabelImagePicker.ForReading(imageUrl, DownloadWidth), cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return Finished(null, "the label image no longer exists");
            if (!response.IsSuccessStatusCode)
                return new(null, $"image download returned {(int)response.StatusCode}", IsTransient: true, 0, 0);

            bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new(null, "image download failed", IsTransient: true, 0, 0);
        }

        var pngPath = Path.Combine(Path.GetTempPath(), $"label-{Guid.NewGuid():N}.png");
        try
        {
            try
            {
                using var image = Image.Load(bytes);
                var scale = Math.Min(2.0, MaxOcrEdge / (double)Math.Max(image.Width, image.Height));
                var width = (int)(image.Width * scale);
                var height = (int)(image.Height * scale);
                image.Mutate(x =>
                {
                    x.Grayscale();
                    if (scale > 1)
                        x.Resize(width, height);
                });
                await image.SaveAsync(pngPath, new PngEncoder(), cancellationToken);
            }
            catch (ImageFormatException)
            {
                return Finished(null, "the image could not be decoded");
            }

            NutritionLabelReading? best = null;
            foreach (var mode in PageSegmentationModes)
            {
                var text = await RunTesseractAsync(pngPath, mode, cancellationToken);
                if (text is null)
                    return Finished(best, "OCR failed");

                var reading = LabelTextParser.Parse(text);
                if (NutritionLabelValidator.Validate(reading, requireCalorieCheck: true).Accepted)
                    return Finished(reading, null);

                if (best is null || (!best.IsNutritionLabel && reading.IsNutritionLabel))
                    best = reading;
            }

            // The service runs the validator again and records its reason.
            return Finished(best, null);
        }
        finally
        {
            try
            {
                File.Delete(pngPath);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless; the container's /tmp is reset on recreate.
            }
        }
    }

    private static NutritionLabelReadResult Finished(NutritionLabelReading? reading, string? error) =>
        new(reading, reading is null ? error ?? "no reading" : error, IsTransient: false, 0, 0);

    private async Task<string?> RunTesseractAsync(string path, int mode, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(options.TesseractPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(path);
        start.ArgumentList.Add("stdout");
        start.ArgumentList.Add("--psm");
        start.ArgumentList.Add(mode.ToString(CultureInfo.InvariantCulture));
        start.Environment["OMP_THREAD_LIMIT"] = "1";

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogError(ex, "Tesseract could not be started.");
            return null;
        }

        if (process is null)
            return null;

        using (process)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(OcrTimeout);

            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var text = await output;
                await errors;
                return process.ExitCode == 0 ? text : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                logger.LogWarning("Tesseract took longer than {Seconds} s on one label; skipped.", OcrTimeout.TotalSeconds);
                return null;
            }
        }
    }

    private static bool CheckInstalled(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            return process is not null && process.WaitForExit(10_000) && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
