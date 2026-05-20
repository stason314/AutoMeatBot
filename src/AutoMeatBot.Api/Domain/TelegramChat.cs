namespace AutoMeatBot.Api.Domain;

public sealed class TelegramChat
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string? Title { get; set; }
    public string? Username { get; set; }
    public string TimeZone { get; set; } = "Europe/Moscow";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

