using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.NutritionLabels;

public sealed class NutritionLabelOptions
{
    /// <summary>
    /// Whether the BACKGROUND JOB writes readings. Off by default: the pilot
    /// endpoint reads without writing, so accuracy and cost are measured first.
    /// </summary>
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-haiku-4-5-20251001";
    /// <summary>Distinct label images per run.</summary>
    public int MaxPerRun { get; set; } = 20;
    public int IntervalMinutes { get; set; } = 60;
    public int ImageWidth { get; set; } = 1200;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed record NutritionLabelReadResult(
    NutritionLabelReading? Reading,
    string? Error,
    // A network error, 429 or 5xx: nothing is recorded, the image is tried
    // again on a later run. Anything else counts as a finished read.
    bool IsTransient,
    int InputTokens,
    int OutputTokens);

/// <summary>Reads one label image with Claude's vision model.</summary>
public sealed class NutritionLabelReader(HttpClient httpClient, NutritionLabelOptions options, ILogger<NutritionLabelReader> logger)
{
    public const string HttpClientName = "anthropic";

    // The model copies, it doesn't compute: every number must be printed on the
    // panel. A value it would have to estimate stays null, which is the same
    // "empty rather than made up" rule every scraper follows.
    private const string Prompt = """
        Read the Nutrition Facts or Supplement Facts panel in this image.
        Copy values exactly as printed. Never estimate, convert or compute a value; use null for anything not printed.
        If the panel has several columns (per serving and per container, or by age group), use the first per-serving column.
        If the image does not show a legible Nutrition Facts or Supplement Facts panel, return {"isNutritionLabel": false}.
        Return ONLY this JSON, no other text:
        {
          "isNutritionLabel": true,
          "panelType": "Nutrition Facts" or "Supplement Facts",
          "servingSizeGrams": number or null (only when the serving size is printed in grams),
          "calories": number or null,
          "proteinGrams": number or null,
          "carbohydrateGrams": number or null (Total Carbohydrate),
          "fiberGrams": number or null,
          "sugarAlcoholGrams": number or null,
          "fatGrams": number or null (Total Fat),
          "rows": [{"label": "Serving Size", "amount": "1 Scoop (36g)"}, {"label": "Protein", "amount": "25g"}]
        }
        "rows" lists every printed row with its amount in order, including the serving size, but not servings per container and not % Daily Value.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<NutritionLabelReadResult> ReadAsync(string imageUrl, string? model, CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
            return new(null, "no API key configured", IsTransient: true, 0, 0);

        var body = new
        {
            model = model ?? options.Model,
            max_tokens = 1500,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "url", url = NutritionLabelImagePicker.ForReading(imageUrl, options.ImageWidth) } },
                        new { type = "text", text = Prompt },
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages") { Content = JsonContent.Create(body) };
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new(null, $"request failed: {ex.GetType().Name}", IsTransient: true, 0, 0);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var transient = response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                // The body is the API's error message; it never contains the key.
                logger.LogWarning("Label read failed with {Status}: {Body}", (int)response.StatusCode, Truncate(json, 300));
                return new(null, $"API returned {(int)response.StatusCode}", transient, 0, 0);
            }

            var envelope = JsonSerializer.Deserialize<MessagesResponse>(json, JsonOptions);
            var text = envelope?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;
            var reading = text is null ? null : ParseReading(text);
            var input = envelope?.Usage?.InputTokens ?? 0;
            var output = envelope?.Usage?.OutputTokens ?? 0;

            return reading is null
                ? new(null, "the answer was not the expected JSON", IsTransient: false, input, output)
                : new(reading, null, IsTransient: false, input, output);
        }
    }

    /// <summary>Takes the JSON object out of the model's answer, fenced or not.</summary>
    internal static NutritionLabelReading? ParseReading(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            return JsonSerializer.Deserialize<NutritionLabelReading>(text[start..(end + 1)], JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed class MessagesResponse
    {
        public List<ContentBlock>? Content { get; set; }
        public UsageBlock? Usage { get; set; }
    }

    private sealed class ContentBlock
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
    }

    private sealed class UsageBlock
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }
}
