using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaChatApp.Services;

public class LlamaService : ILLamaService
{
    private readonly ILogger<LlamaService> _logger;
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private ChatSession? _session;
    private string _modelPath = string.Empty;
    private ModelParams? _modelParams;

    public bool IsModelLoaded => _model != null;
    public string ModelName => !string.IsNullOrEmpty(_modelPath) ? Path.GetFileName(_modelPath) : "No Model Loaded";
    public string ModelPath => _modelPath;

    public LlamaService(ILogger<LlamaService> logger)
    {
        _logger = logger;
    }

    public async Task LoadModelAsync(string modelPath, ModelParams parameters)
    {
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            _logger.LogError("Model file not found at {Path}", modelPath);
            throw new FileNotFoundException("Model file not found", modelPath);
        }

        _logger.LogInformation("Loading model from {Path} with context size {ContextSize} and {GpuLayers} GPU layers", 
            modelPath, parameters.ContextSize, parameters.GpuLayerCount);

        var sw = Stopwatch.StartNew();
        try
        {
            await Task.Run(() =>
            {
                UnloadModel();
                _modelParams = parameters; 
                _model = LLamaWeights.LoadFromFile(parameters);
                _modelPath = modelPath;
                _context = _model.CreateContext(parameters);
            });
            sw.Stop();
            _logger.LogInformation("Model loaded successfully in {Elapsed}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model from {Path}", modelPath);
            throw;
        }
    }

    public void UnloadModel()
    {
        if (_model != null)
        {
            _logger.LogInformation("Unloading model {ModelName}", ModelName);
        }
        _context?.Dispose();
        _context = null;
        _model?.Dispose();
        _model = null;
        _session = null;
        _modelPath = string.Empty;
        _modelParams = null;

        // Force Garbage Collection to reclaim memory immediately
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void InitializeSession(string systemPrompt)
    {
        if (_model == null || _context == null) throw new InvalidOperationException("Model or Context not loaded.");

        _logger.LogDebug("Initializing new session with system prompt");
        var executor = new InteractiveExecutor(_context);
        var history = new ChatHistory();
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            history.AddMessage(AuthorRole.System, systemPrompt);
        }
        _session = new ChatSession(executor, history);
    }

    public void RestoreSession(string systemPrompt, IEnumerable<(string Author, string Text)> historyMessages)
    {
        if (_model == null || _modelParams == null) throw new InvalidOperationException("Model not loaded.");

        _logger.LogDebug("Restoring session with {Count} history messages", historyMessages.Count());
        var sw = Stopwatch.StartNew();

        _context?.Dispose();
        _context = _model.CreateContext(_modelParams);

        var executor = new InteractiveExecutor(_context);
        var history = new ChatHistory();
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            history.AddMessage(AuthorRole.System, systemPrompt);
        }

        foreach (var msg in historyMessages)
        {
            if (msg.Author == "User")
                history.AddMessage(AuthorRole.User, msg.Text);
            else if (msg.Author == "Assistant")
                history.AddMessage(AuthorRole.Assistant, msg.Text);
            else if (msg.Author == "System" || msg.Author == "Error")
                history.AddMessage(AuthorRole.System, msg.Text);
        }

        _session = new ChatSession(executor, history);
        sw.Stop();
        _logger.LogDebug("Session restored in {Elapsed}ms", sw.ElapsedMilliseconds);
    }

    public async IAsyncEnumerable<string> GenerateResponseAsync(string prompt, InferenceParams inferenceParams, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var (text, _) in GenerateResponseWithMetricsAsync(prompt, inferenceParams, cancellationToken))
        {
            yield return text;
        }
    }

    public async IAsyncEnumerable<(string Text, GenerationMetrics Metrics)> GenerateResponseWithMetricsAsync(string prompt, InferenceParams inferenceParams, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_session == null) throw new InvalidOperationException("Session not initialized.");

        _logger.LogInformation("Starting generation for prompt: {PromptPreview}...", prompt.Length > 50 ? prompt[..50] : prompt);
        
        var userMessage = new ChatHistory.Message(AuthorRole.User, prompt);
        var metrics = new GenerationMetrics();
        var sw = Stopwatch.StartNew();
        int tokenCount = 0;

        await foreach (var text in _session.ChatAsync(userMessage, inferenceParams, cancellationToken))
        {
            tokenCount++;
            metrics.TokenCount = tokenCount;
            metrics.ElapsedTime = sw.Elapsed;
            
            if (sw.Elapsed.TotalSeconds > 0)
            {
                metrics.TokensPerSecond = tokenCount / sw.Elapsed.TotalSeconds;
            }

            yield return (text, metrics);
        }

        sw.Stop();
        _logger.LogInformation("Generation completed: {TokenCount} tokens in {Elapsed}s ({TPS:F2} t/s)", 
            tokenCount, sw.Elapsed.TotalSeconds, metrics.TokensPerSecond);
    }

    public void SaveState(string filePath)
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized.");
        _logger.LogInformation("Saving KV cache to {FilePath}", filePath);
        _context.SaveState(filePath);
    }

    public void LoadState(string filePath, string systemPrompt, IEnumerable<(string Author, string Text)> historyMessages)
    {
        if (_context == null) throw new InvalidOperationException("Context not initialized.");
        if (!File.Exists(filePath)) throw new FileNotFoundException("State file not found.", filePath);
        _logger.LogInformation("Loading KV cache from {FilePath}", filePath);
        _context.LoadState(filePath);
        
        // Re-initialize session with existing context, executor, and history
        var executor = new InteractiveExecutor(_context);
        var history = new ChatHistory();
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            history.AddMessage(AuthorRole.System, systemPrompt);
        }

        foreach (var msg in historyMessages)
        {
            if (msg.Author == "User")
                history.AddMessage(AuthorRole.User, msg.Text);
            else if (msg.Author == "Assistant")
                history.AddMessage(AuthorRole.Assistant, msg.Text);
            else if (msg.Author == "System" || msg.Author == "Error")
                history.AddMessage(AuthorRole.System, msg.Text);
        }

        _session = new ChatSession(executor, history);
    }

    public int CountTokens(string text)
    {
        if (_context == null || string.IsNullOrEmpty(text)) return 0;
        try
        {
            return _context.Tokenize(text).Length;
        }
        catch
        {
            // Fallback to rough estimation (approx 4 chars per token)
            return text.Length / 4;
        }
    }

    public void Dispose()
    {
        UnloadModel();
    }
}
