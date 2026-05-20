using AutoMeatBot.Api.Domain;
using AutoMeatBot.Api.Dtos;
using AutoMeatBot.Api.Options;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoMeatBot.Api.Services;

public sealed class DeepSeekMeetingExtractor(
    HttpClient httpClient,
    IOptions<DeepSeekOptions> options,
    IOptions<MeetingExtractionOptions> extractionOptions,
    ILogger<DeepSeekMeetingExtractor> logger) : IMeetingExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiMeetingExtraction> ExtractAsync(
        TelegramChat chat,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var deepSeek = options.Value;
        if (!deepSeek.Enabled || messages.Count == 0)
        {
            return new AiMeetingExtraction();
        }

        if (string.IsNullOrWhiteSpace(deepSeek.ApiKey))
        {
            logger.LogWarning("DeepSeek API key is not configured");
            return new AiMeetingExtraction();
        }

        var baseUrl = deepSeek.BaseUrl.TrimEnd('/');
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(deepSeek.TimeoutSeconds, 10, 180)));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deepSeek.ApiKey);
        request.Content = JsonContent.Create(new DeepSeekChatRequest
        {
            Model = deepSeek.Model,
            Stream = false,
            Temperature = 0.1,
            MaxTokens = 1200,
            ResponseFormat = new DeepSeekResponseFormat("json_object"),
            Thinking = new DeepSeekThinking("disabled"),
            Messages =
            [
                new DeepSeekMessage("system", BuildSystemPrompt(chat.TimeZone)),
                new DeepSeekMessage("user", BuildUserPrompt(chat, messages))
            ]
        }, options: JsonOptions);

        DeepSeekChatResponse? deepSeekResponse;
        try
        {
            using var response = await httpClient.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("DeepSeek request failed with {StatusCode}: {Error}", response.StatusCode, error);
                return new AiMeetingExtraction();
            }

            deepSeekResponse = await response.Content.ReadFromJsonAsync<DeepSeekChatResponse>(JsonOptions, timeoutCts.Token);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "DeepSeek API is unavailable");
            return new AiMeetingExtraction();
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "DeepSeek request timed out");
            return new AiMeetingExtraction();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not parse DeepSeek response");
            return new AiMeetingExtraction();
        }

        var content = deepSeekResponse?.Choices.FirstOrDefault()?.Message?.Content;
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
  "is_new_meeting": false,
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
- Focus on the latest message first. Use earlier messages as context only.
- If the latest message introduces another/additional/separate meeting, extract that new meeting instead of updating the previous one and set "is_new_meeting": true.
- Russian phrases like "еще", "дополнительно", "другой вопрос", "отдельно", "на следующей неделе" can indicate a separate meeting when they appear in the latest message.
- A meeting can be only a proposal, not final yet.
- Prefer final agreed time over earlier proposed times.
- "договорились", "ок", "подтверждаю", "всем ок", "созвонимся", "соберемся", and similar phrases can mean meeting intent or confirmation.
- Extract Zoom, Google Meet, Teams, Yandex Telemost, Telegram call, and generic URLs.
- If the date is relative, resolve it using current UTC time {{now:O}} and chat timezone {{chatTimeZone}}.
- If timezone is absent, use {{chatTimeZone}} or {{defaultTimeZone}}.
- Use null for unknown fields.
- If there is no meeting discussion, return {"has_meeting":false,"is_new_meeting":false,"participants":[],"confidence":0.0}.
""";
    }

    private static string BuildUserPrompt(TelegramChat chat, IReadOnlyList<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Chat: {chat.Title ?? chat.Username ?? chat.Id.ToString()}");
        builder.AppendLine($"Timezone: {chat.TimeZone}");
        builder.AppendLine($"Latest message id: {messages.Last().TelegramMessageId}");
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

    private sealed class DeepSeekChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("response_format")]
        public DeepSeekResponseFormat ResponseFormat { get; set; } = new("json_object");

        [JsonPropertyName("thinking")]
        public DeepSeekThinking Thinking { get; set; } = new("disabled");

        [JsonPropertyName("messages")]
        public List<DeepSeekMessage> Messages { get; set; } = [];
    }

    private sealed record DeepSeekMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record DeepSeekResponseFormat(
        [property: JsonPropertyName("type")] string Type);

    private sealed record DeepSeekThinking(
        [property: JsonPropertyName("type")] string Type);

    private sealed class DeepSeekChatResponse
    {
        [JsonPropertyName("choices")]
        public List<DeepSeekChoice> Choices { get; set; } = [];
    }

    private sealed class DeepSeekChoice
    {
        [JsonPropertyName("message")]
        public DeepSeekMessage? Message { get; set; }
    }
}
