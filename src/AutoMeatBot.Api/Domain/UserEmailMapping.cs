namespace AutoMeatBot.Api.Domain;

public sealed class UserEmailMapping
{
    public Guid Id { get; set; }
    public long? TelegramUserId { get; set; }
    public TelegramUser? TelegramUser { get; set; }
    public string? TelegramUsername { get; set; }
    public string? DisplayName { get; set; }
    public string Email { get; set; } = "";
    public string Source { get; set; } = "auto";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

