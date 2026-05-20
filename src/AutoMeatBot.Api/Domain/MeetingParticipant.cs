namespace AutoMeatBot.Api.Domain;

public sealed class MeetingParticipant
{
    public Guid Id { get; set; }
    public Guid MeetingCandidateId { get; set; }
    public MeetingCandidate? MeetingCandidate { get; set; }
    public long? TelegramUserId { get; set; }
    public TelegramUser? TelegramUser { get; set; }
    public string? TelegramUsername { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "required";
    public ParticipantResponse Response { get; set; } = ParticipantResponse.Unknown;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

