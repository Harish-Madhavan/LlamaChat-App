// AppSettings.cs
namespace LlamaChatApp
{
    public class AppSettings
    {
        public string? LastModelPath { get; set; }
        public double Temperature { get; set; } = 0.7;
        public double TopP { get; set; } = 0.9;
        public int MaxTokens { get; set; } = 512;
        public int GpuLayerCount { get; set; } = 30; // New
        public int ContextSize { get; set; } = 2048; // New
        public string SystemPrompt { get; set; } = "You are a helpful, kind, and honest assistant. You always provide clear and concise answers."; // New
    }
}