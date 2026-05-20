using System.Text.Json.Serialization;

namespace AutoMeatBot.Api.Dtos;

public sealed class AiMeetingExtraction
{
    [JsonPropertyName("has_meeting")]
    public bool HasMeeting { get; set; }

    [JsonPropertyName("meeting_topic")]
    public string? MeetingTopic { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("proposed_datetime")]
    public DateTimeOffset? ProposedDateTime { get; set; }

    [JsonPropertyName("timezone")]
    public string? TimeZone { get; set; }

    [JsonPropertyName("meeting_url")]
    public string? MeetingUrl { get; set; }

    [JsonPropertyName("participants")]
    public List<AiParticipant> Participants { get; set; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public sealed class AiParticipant
{
    [JsonPropertyName("telegram_user_id")]
    public long? TelegramUserId { get; set; }

    [JsonPropertyName("telegram_username")]
    public string? TelegramUsername { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("response")]
    public string? Response { get; set; }
}

