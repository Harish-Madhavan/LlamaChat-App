// AppSettings.cs
using LlamaChatApp.Commands;

namespace LlamaChatApp
{
    public class AppSettings
    {
        public string? LastModelPath { get; set; }
        public double Temperature { get; set; } = AppConstants.DEFAULT_TEMPERATURE;
        public double TopP { get; set; } = AppConstants.DEFAULT_TOP_P;
        public int MaxTokens { get; set; } = AppConstants.DEFAULT_MAX_TOKENS;
        public int GpuLayerCount { get; set; } = AppConstants.DEFAULT_GPU_LAYERS;
        public int ContextSize { get; set; } = AppConstants.DEFAULT_CONTEXT_SIZE;
        public string SystemPrompt { get; set; } = AppConstants.DEFAULT_SYSTEM_PROMPT;
    }
}