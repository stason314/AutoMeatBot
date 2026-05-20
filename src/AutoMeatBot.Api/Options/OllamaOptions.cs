namespace AutoMeatBot.Api.Options;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:7b";
    public bool Enabled { get; set; } = true;
}

