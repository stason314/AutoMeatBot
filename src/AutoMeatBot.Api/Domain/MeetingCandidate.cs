namespace AutoMeatBot.Api.Domain;

public sealed class MeetingCandidate
{
    public Guid Id { get; set; }
    public long ChatId { get; set; }
    public TelegramChat? Chat { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Draft;
    public string? Topic { get; set; }
    public DateTime? ProposedStartUtc { get; set; }
    public string TimeZone { get; set; } = "Europe/Moscow";
    public string? MeetingUrl { get; set; }
    public double Confidence { get; set; }
    public string? AiReason { get; set; }
    public int? SourceFirstMessageId { get; set; }
    public int? SourceLastMessageId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<MeetingParticipant> Participants { get; set; } = [];
}

