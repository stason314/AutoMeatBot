namespace AutoMeatBot.Api.Domain;

public sealed class BusinessConnectionRecord
{
    public string Id { get; set; } = "";
    public long UserChatId { get; set; }
    public bool IsEnabled { get; set; }
    public string RightsJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

