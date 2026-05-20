namespace AutoMeatBot.Api.Domain;

public sealed class TelegramUser
{
    public long Id { get; set; }
    public bool IsBot { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}

