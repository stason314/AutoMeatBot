using AutoMeatBot.Api.Domain;
using AutoMeatBot.Api.Dtos;
using AutoMeatBot.Api.Options;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoMeatBot.Api.Services;

public sealed class OllamaMeetingExtractor(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    IOptions<MeetingExtractionOptions> extractionOptions,
    ILogger<OllamaMeetingExtractor> logger) : IMeetingExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiMeetingExtraction> ExtractAsync(
        TelegramChat chat,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var ollama = options.Value;
        if (!ollama.Enabled || messages.Count == 0)
        {
            return new AiMeetingExtraction();
        }

        var baseUrl = ollama.BaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat");
        request.Content = JsonContent.Create(new OllamaChatRequest
        {
            Model = ollama.Model,
            Stream = false,
            Format = "json",
            Messages =
            [
                new OllamaMessage("system", BuildSystemPrompt(chat.TimeZone)),
                new OllamaMessage("user", BuildUserPrompt(chat, messages))
            ]
        }, options: JsonOptions);

        OllamaChatResponse? ollamaResponse;
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama request failed with {StatusCode}", response.StatusCode);
                return new AiMeetingExtraction();
            }

            ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Ollama is unavailable");
            return new AiMeetingExtraction();
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama request timed out");
            return new AiMeetingExtraction();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not parse Ollama response");
            return new AiMeetingExtraction();
        }

        var content = ollamaResponse?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            return new AiMeetingExtraction();
        }

        try
        {
            var json = NormalizeJson(content);
            return JsonSerializer.Deserialize<AiMeetingExtraction>(json, JsonOptions) ?? new AiMeetingExtraction();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not parse LLM JSON: {Content}", content);
            return new AiMeetingExtraction();
        }
    }

    private string BuildSystemPrompt(string chatTimeZone)
    {
        var now = DateTimeOffset.UtcNow;
        var defaultTimeZone = extractionOptions.Value.DefaultTimeZone;

        return $$"""
You extract online meeting candidates from Telegram group discussions.

Return strict JSON only, without markdown.

Use this schema:
{
  "has_meeting": true,
  "meeting_topic": "short topic or null",
  "status": "draft|negotiating|proposed|confirmed_by_ai|cancelled",
  "proposed_datetime": "ISO-8601 datetime with timezone offset or null",
  "timezone": "IANA timezone",
  "meeting_url": "URL or null",
  "participants": [
    {
      "telegram_user_id": 123,
      "telegram_username": "username or null",
      "display_name": "name or null",
      "role": "required|optional",
      "response": "unknown|invited|accepted|declined|tentative"
    }
  ],
  "confidence": 0.0,
  "reason": "short explanation"
}

Rules:
- Detect Russian and English meeting discussions.
- A meeting can be only a proposal, not final yet.
- Prefer final agreed time over earlier proposed times.
- "dogovorilis", "ok", "podtverzhdayu", "vsem ok", and similar phrases can mean confirmation.
- Extract Zoom, Google Meet, Teams, Yandex Telemost, Telegram call, and generic URLs.
- If the date is relative, resolve it using current UTC time {{now:O}} and chat timezone {{chatTimeZone}}.
- If timezone is absent, use {{chatTimeZone}} or {{defaultTimeZone}}.
- Use null for unknown fields.
- If there is no meeting discussion, return {"has_meeting":false,"participants":[],"confidence":0.0}.
""";
    }

    private static string BuildUserPrompt(TelegramChat chat, IReadOnlyList<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Chat: {chat.Title ?? chat.Username ?? chat.Id.ToString()}");
        builder.AppendLine($"Timezone: {chat.TimeZone}");
        builder.AppendLine("Messages:");

        foreach (var message in messages)
        {
            var sender = message.SenderUser is null
                ? "unknown"
                : $"{message.SenderUser.DisplayName} (@{message.SenderUser.Username ?? "none"}, id:{message.SenderUser.Id})";

            builder.AppendLine($"[{message.TelegramMessageId}] {message.SentAtUtc:O} {sender}: {message.Text}");
        }

        return builder.ToString();
    }

    private static string NormalizeJson(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed.Trim('`').Trim();
            if (trimmed.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[4..].Trim();
            }
        }

        var first = trimmed.IndexOf('{');
        var last = trimmed.LastIndexOf('}');
        return first >= 0 && last > first ? trimmed[first..(last + 1)] : trimmed;
    }

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; } = "json";

        [JsonPropertyName("messages")]
        public List<OllamaMessage> Messages { get; set; } = [];
    }

    private sealed record OllamaMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }
    }
}
