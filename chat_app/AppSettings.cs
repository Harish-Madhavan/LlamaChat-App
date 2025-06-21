// AppSettings.cs
namespace LlamaChatApp
{
    public class AppSettings
    {
        public string? LastModelPath { get; set; }
        public double Temperature { get; set; } = 0.7;
        public double TopP { get; set; } = 0.9;
        public int MaxTokens { get; set; } = 512;
    }
}