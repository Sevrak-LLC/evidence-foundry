using System.ClientModel;
using System.Net.Http;
using System.Text.Json;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Images;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using Serilog;

namespace EvidenceFoundry.Services;

public partial class OpenAIService
{
    public static TimeSpan? RateLimitDelayOverride { get; set; }

    private readonly OpenAIClient _client;
    private readonly ChatClient _chatClient;
    private readonly Random _rng;
    private readonly AIModelConfig? _modelConfig;
    private readonly TokenUsageTracker? _usageTracker;
    private readonly ILogger _logger;
    private const int MaxRetries = 4;
    private static readonly int[] TransientStatusCodes = { 408, 429, 500, 502, 503, 504 };

    private static OpenAIClient CreateClient(string apiKey)
    {
        var options = new OpenAIClientOptions();
        options.NetworkTimeout = TimeSpan.FromMinutes(10);
        return new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);
    }

    public OpenAIService(string apiKey, string model, Random rng, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required.", nameof(model));
        ArgumentNullException.ThrowIfNull(rng);
        _rng = rng;
        _client = CreateClient(apiKey);
        _chatClient = _client.GetChatClient(model);
        _logger = (logger ?? Serilog.Log.Logger).ForContext<OpenAIService>();
        Log.OpenAIServiceInitialized(_logger, model);
    }

    public OpenAIService(
        string apiKey,
        AIModelConfig modelConfig,
        TokenUsageTracker? usageTracker,
        Random rng,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));
        ArgumentNullException.ThrowIfNull(modelConfig);
        if (string.IsNullOrWhiteSpace(modelConfig.ModelId))
            throw new ArgumentException("Model ID is required.", nameof(modelConfig));
        ArgumentNullException.ThrowIfNull(rng);
        _rng = rng;
        _modelConfig = modelConfig;
        _usageTracker = usageTracker;
        _client = CreateClient(apiKey);
        _chatClient = _client.GetChatClient(modelConfig.ModelId);
        _logger = (logger ?? Serilog.Log.Logger).ForContext<OpenAIService>();
        Log.OpenAIServiceInitialized(_logger, modelConfig.ModelId);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        Log.TestingOpenAIConnection(_logger);
        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a helpful assistant."),
                new UserChatMessage("Say 'connected' if you can read this.")
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 10
            };

            var response = await _chatClient.CompleteChatAsync(messages, options, ct);
            return response.Value.Content.Count > 0;
        }
        catch (Exception ex)
        {
            Log.OpenAIConnectionTestFailed(_logger, ex);
            return false;
        }
    }

    public async Task<string> GetCompletionAsync(
        string systemPrompt,
        string userPrompt,
        string? operationName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
            throw new ArgumentException("System prompt is required.", nameof(systemPrompt));
        if (string.IsNullOrWhiteSpace(userPrompt))
            throw new ArgumentException("User prompt is required.", nameof(userPrompt));

        var operation = operationName ?? "Completion";
        Log.RequestingOpenAICompletion(_logger, operation);

        Exception lastException = null!;
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userPrompt)
                };

                var options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = GetMaxOutputTokens(isJson: false)
                };

                var response = await _chatClient.CompleteChatAsync(messages, options, ct);

                if (response.Value.Content.Count > 0)
                {
                    // Track token usage if configured
                    TrackUsage(operation, response.Value.Usage);

                    return response.Value.Content[0].Text;
                }

                throw new InvalidOperationException("Empty response from OpenAI");
            }
            catch (Exception ex) when (IsRetryable(ex, ct))
            {
                lastException = ex;
                await DelayForRetryAsync(attempt, ex, ct);
            }
        }

        Log.OpenAICompletionFailed(_logger, MaxRetries, operation, lastException);
        throw new InvalidOperationException(
            $"Failed to get response from OpenAI after multiple attempts. {lastException.Message}",
            lastException);
    }

    public async Task<T?> GetJsonCompletionAsync<T>(
        string systemPrompt,
        string userPrompt,
        string? operationName = null,
        CancellationToken ct = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
            throw new ArgumentException("System prompt is required.", nameof(systemPrompt));
        if (string.IsNullOrWhiteSpace(userPrompt))
            throw new ArgumentException("User prompt is required.", nameof(userPrompt));

        var operation = operationName ?? "JSON Completion";
        Log.RequestingOpenAIJsonCompletion(_logger, operation);

        Exception lastException = null!;
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(PromptScaffolding.AppendJsonOnlyInstruction(systemPrompt)),
                    new UserChatMessage(userPrompt)
                };

                var options = new ChatCompletionOptions
                {
                    ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                    MaxOutputTokenCount = GetMaxOutputTokens(isJson: true)
                };

                var response = await _chatClient.CompleteChatAsync(messages, options, ct);

                if (response.Value.Content.Count > 0)
                {
                    // Track token usage if configured
                    TrackUsage(operation, response.Value.Usage);

                    var json = response.Value.Content[0].Text;
                    return JsonSerializer.Deserialize<T>(json, JsonSerializationDefaults.CaseInsensitive);
                }

                throw new InvalidOperationException("Empty response from OpenAI");
            }
            catch (JsonException ex)
            {
                lastException = ex;
                // JSON parsing failed, retry
                if (attempt == MaxRetries - 1)
                    throw;

                await DelayForRetryAsync(attempt, ex, ct);
            }
            catch (Exception ex) when (IsRetryable(ex, ct))
            {
                lastException = ex;
                await DelayForRetryAsync(attempt, ex, ct);
            }
        }

        Log.OpenAIJsonCompletionFailed(_logger, MaxRetries, operation, lastException);
        throw new InvalidOperationException(
            $"Failed to get valid JSON response from OpenAI after multiple attempts. {lastException.Message}",
            lastException);
    }

    // Legacy overloads for backward compatibility
    public Task<T?> GetJsonCompletionAsync<T>(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct) where T : class
        => GetJsonCompletionAsync<T>(systemPrompt, userPrompt, null, ct);

    private void TrackUsage(string operation, ChatTokenUsage? usage)
    {
        if (_usageTracker != null && _modelConfig != null && usage != null)
        {
            _usageTracker.RecordUsage(
                operation,
                _modelConfig,
                usage.InputTokenCount,
                usage.OutputTokenCount);
        }
    }

    private int GetMaxOutputTokens(bool isJson)
    {
        if (_modelConfig != null)
        {
            var configured = isJson ? _modelConfig.MaxJsonOutputTokens : _modelConfig.MaxOutputTokens;
            if (configured > 0)
                return configured;
        }

        return isJson ? AIModelConfig.DefaultMaxJsonOutputTokens : AIModelConfig.DefaultMaxOutputTokens;
    }

    public async Task<byte[]?> GenerateImageAsync(
        string prompt,
        string? operationName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required.", nameof(prompt));

        var operation = operationName ?? "Image Generation";
        Log.RequestingOpenAIImageGeneration(_logger, operation);

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var imageClient = _client.GetImageClient("dall-e-3");

                var options = new ImageGenerationOptions
                {
                    Quality = GeneratedImageQuality.Standard,
                    Size = GeneratedImageSize.W1024xH1024,
                    ResponseFormat = GeneratedImageFormat.Bytes
                };

                var response = await imageClient.GenerateImageAsync(prompt, options, ct);

                if (response.Value?.ImageBytes != null)
                {
                    return response.Value.ImageBytes.ToArray();
                }

                return null;
            }
            catch (ClientResultException ex) when (ex.Status == 400)
            {
                // Content policy violation or invalid prompt - don't retry
                Log.OpenAIImageGenerationRejected(_logger, operation, ex);
                return null;
            }
            catch (Exception ex) when (IsRetryable(ex, ct))
            {
                await DelayForRetryAsync(attempt, ex, ct);
            }
        }

        Log.OpenAIImageGenerationFailed(_logger, MaxRetries, operation);
        return null;
    }

    /// <summary>
    /// Generate speech audio from text using OpenAI TTS
    /// </summary>
    /// <param name="text">The text to convert to speech</param>
    /// <param name="voice">Voice to use: alloy, echo, fable, onyx, nova, shimmer</param>
    /// <param name="operationName">Optional operation name for tracking</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>MP3 audio bytes, or null on failure</returns>
    public async Task<byte[]?> GenerateSpeechAsync(
        string text,
        string voice = "alloy",
        string? operationName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required.", nameof(text));
        if (string.IsNullOrWhiteSpace(voice))
            throw new ArgumentException("Voice is required.", nameof(voice));

        var operation = operationName ?? "Speech Generation";
        Log.RequestingOpenAISpeechGeneration(_logger, operation);

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var audioClient = _client.GetAudioClient("tts-1");

                var options = new SpeechGenerationOptions
                {
                    SpeedRatio = 1.0f,
                    ResponseFormat = GeneratedSpeechFormat.Mp3
                };

                // Map voice string to enum
                var voiceEnum = voice.ToLowerInvariant() switch
                {
                    "echo" => GeneratedSpeechVoice.Echo,
                    "fable" => GeneratedSpeechVoice.Fable,
                    "onyx" => GeneratedSpeechVoice.Onyx,
                    "nova" => GeneratedSpeechVoice.Nova,
                    "shimmer" => GeneratedSpeechVoice.Shimmer,
                    _ => GeneratedSpeechVoice.Alloy
                };

                var response = await audioClient.GenerateSpeechAsync(text, voiceEnum, options, ct);

                if (response.Value != null)
                {
                    // BinaryData has a ToArray() method directly
                    return response.Value.ToArray();
                }

                return null;
            }
            catch (ClientResultException ex) when (ex.Status == 400)
            {
                // Invalid request - don't retry
                Log.OpenAISpeechGenerationRejected(_logger, operation, ex);
                return null;
            }
            catch (Exception ex) when (IsRetryable(ex, ct))
            {
                await DelayForRetryAsync(attempt, ex, ct);
            }
        }

        Log.OpenAISpeechGenerationFailed(_logger, MaxRetries, operation);
        return null;
    }

    private static bool IsRetryable(Exception ex, CancellationToken ct)
    {
        if (ex is ClientResultException cre)
            return TransientStatusCodes.Contains(cre.Status);

        if (ex is HttpRequestException)
            return true;

        if (ex is TaskCanceledException && !ct.IsCancellationRequested)
            return true;

        return false;
    }

    private TimeSpan GetRetryDelay(int attempt, Exception ex)
    {
        if (ex is ClientResultException { Status: 429 } && RateLimitDelayOverride.HasValue)
            return RateLimitDelayOverride.Value;

        var baseSeconds = Math.Pow(2, attempt + 1);
        var jitter = 0.8 + (_rng.NextDouble() * 0.4);
        return TimeSpan.FromSeconds(baseSeconds * jitter);
    }

    private Task DelayForRetryAsync(int attempt, Exception ex, CancellationToken ct)
    {
        var delay = GetRetryDelay(attempt, ex);
        int? statusCode = ex is ClientResultException cre ? cre.Status : null;
        Log.RetryingOpenAIRequest(
            _logger,
            attempt + 1,
            MaxRetries,
            (int)delay.TotalMilliseconds,
            statusCode,
            ex);
        return Task.Delay(delay, ct);
    }

    private static class Log
    {
        public static void OpenAIServiceInitialized(ILogger logger, string modelId)
            => logger.Debug("OpenAIService initialized with model {ModelId}.", modelId);

        public static void TestingOpenAIConnection(ILogger logger)
            => logger.Information("Testing OpenAI connection.");

        public static void OpenAIConnectionTestFailed(ILogger logger, Exception exception)
            => logger.Warning(exception, "OpenAI connection test failed.");

        public static void RequestingOpenAICompletion(ILogger logger, string operationName)
            => logger.Information("Requesting OpenAI completion for {OperationName}.", operationName);

        public static void OpenAICompletionFailed(ILogger logger, int attemptCount, string operationName, Exception exception)
            => logger.Error(
                exception,
                "OpenAI completion failed after {AttemptCount} attempts for {OperationName}.",
                attemptCount,
                operationName);

        public static void RequestingOpenAIJsonCompletion(ILogger logger, string operationName)
            => logger.Information("Requesting OpenAI JSON completion for {OperationName}.", operationName);

        public static void OpenAIJsonCompletionFailed(ILogger logger, int attemptCount, string operationName, Exception exception)
            => logger.Error(
                exception,
                "OpenAI JSON completion failed after {AttemptCount} attempts for {OperationName}.",
                attemptCount,
                operationName);

        public static void RequestingOpenAIImageGeneration(ILogger logger, string operationName)
            => logger.Information("Requesting OpenAI image generation for {OperationName}.", operationName);

        public static void OpenAIImageGenerationRejected(ILogger logger, string operationName, Exception exception)
            => logger.Warning(exception, "OpenAI image generation rejected request for {OperationName}.", operationName);

        public static void OpenAIImageGenerationFailed(ILogger logger, int attemptCount, string operationName)
            => logger.Warning(
                "OpenAI image generation failed after {AttemptCount} attempts for {OperationName}.",
                attemptCount,
                operationName);

        public static void RequestingOpenAISpeechGeneration(ILogger logger, string operationName)
            => logger.Information("Requesting OpenAI speech generation for {OperationName}.", operationName);

        public static void OpenAISpeechGenerationRejected(ILogger logger, string operationName, Exception exception)
            => logger.Warning(exception, "OpenAI speech generation rejected request for {OperationName}.", operationName);

        public static void OpenAISpeechGenerationFailed(ILogger logger, int attemptCount, string operationName)
            => logger.Warning(
                "OpenAI speech generation failed after {AttemptCount} attempts for {OperationName}.",
                attemptCount,
                operationName);

        public static void RetryingOpenAIRequest(
            ILogger logger,
            int attempt,
            int maxAttempts,
            int delayMs,
            int? statusCode,
            Exception exception)
            => logger.Warning(
                exception,
                "Retrying OpenAI request (attempt {Attempt}/{MaxAttempts}) after {DelayMs} ms. StatusCode: {StatusCode}.",
                attempt,
                maxAttempts,
                delayMs,
                statusCode);
    }
}
