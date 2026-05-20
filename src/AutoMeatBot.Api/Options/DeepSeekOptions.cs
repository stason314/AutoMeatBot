namespace AutoMeatBot.Api.Options;

public sealed class DeepSeekOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-v4-flash";
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 60;
}
