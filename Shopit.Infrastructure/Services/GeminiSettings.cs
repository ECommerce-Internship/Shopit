namespace Shopit.Infrastructure.Services;

public class GeminiSettings
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-2.5-flash";
}