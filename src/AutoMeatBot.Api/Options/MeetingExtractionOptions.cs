namespace AutoMeatBot.Api.Options;

public sealed class MeetingExtractionOptions
{
    public string DefaultTimeZone { get; set; } = "Europe/Moscow";
    public int WindowSize { get; set; } = 40;
}

