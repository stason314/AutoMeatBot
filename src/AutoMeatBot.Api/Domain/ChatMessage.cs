namespace AutoMeatBot.Api.Domain;

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public long ChatId { get; set; }
    public TelegramChat? Chat { get; set; }
    public int TelegramMessageId { get; set; }
    public long? SenderUserId { get; set; }
    public TelegramUser? SenderUser { get; set; }
    public string? BusinessConnectionId { get; set; }
    public bool IsBusinessMessage { get; set; }
    public string Text { get; set; } = "";
    public string RawUpdateJson { get; set; } = "";
    public DateTime SentAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

