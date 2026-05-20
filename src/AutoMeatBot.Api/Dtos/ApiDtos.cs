namespace AutoMeatBot.Api.Dtos;

public sealed record MeetingDto(
    Guid Id,
    string Status,
    string? Topic,
    DateTime? ProposedStartUtc,
    string TimeZone,
    string? MeetingUrl,
    double Confidence,
    string? AiReason,
    long ChatId,
    string? ChatTitle,
    int? SourceFirstMessageId,
    int? SourceLastMessageId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<MeetingParticipantDto> Participants);

public sealed record MeetingParticipantDto(
    Guid Id,
    long? TelegramUserId,
    string? TelegramUsername,
    string? DisplayName,
    string? Email,
    string Role,
    string Response);

public sealed class MeetingUpdateRequest
{
    public string? Topic { get; set; }
    public DateTime? ProposedStartUtc { get; set; }
    public string? TimeZone { get; set; }
    public string? MeetingUrl { get; set; }
}

public sealed class ParticipantCreateRequest
{
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
}

public sealed record PersonMappingDto(
    Guid Id,
    long? TelegramUserId,
    string? TelegramUsername,
    string? DisplayName,
    string Email,
    string Source,
    DateTime UpdatedAtUtc);

public sealed class PersonMappingCreateRequest
{
    public string? TelegramUsername { get; set; }
    public string? DisplayName { get; set; }
    public string Email { get; set; } = "";
}

public sealed class PersonMappingUpdateRequest
{
    public string? TelegramUsername { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
}

