using LLama.Common;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaChatApp.Services;

public struct GenerationMetrics
{
    public double TokensPerSecond { get; set; }
    public int TokenCount { get; set; }
    public TimeSpan ElapsedTime { get; set; }
}

public interface ILLamaService : IDisposable
{
    bool IsModelLoaded { get; }
    string ModelName { get; }
    string ModelPath { get; }

    Task LoadModelAsync(string modelPath, ModelParams parameters);
    void UnloadModel();

    void InitializeSession(string systemPrompt);
    void RestoreSession(string systemPrompt, IEnumerable<(string Author, string Text)> history);

    IAsyncEnumerable<string> GenerateResponseAsync(string prompt, InferenceParams inferenceParams, CancellationToken cancellationToken);
    IAsyncEnumerable<(string Text, GenerationMetrics Metrics)> GenerateResponseWithMetricsAsync(string prompt, InferenceParams inferenceParams, CancellationToken cancellationToken);

    int CountTokens(string text);
    void SaveState(string filePath);
    void LoadState(string filePath, string systemPrompt, IEnumerable<(string Author, string Text)> history);
}
