using System.ClientModel;
using System.Net.Http;
using System.Text.Json;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Images;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class OpenAIService
{
    public static TimeSpan? RateLimitDelayOverride { get; set; }

    private readonly OpenAIClient _client;
    private readonly ChatClient _chatClient;
    private readonly AIModelConfig? _modelConfig;
    private readonly TokenUsageTracker? _usageTracker;
    private const int MaxRetries = 4;
    private static readonly int[] TransientStatusCodes = { 408, 429, 500, 502, 503, 504 };

    private static OpenAIClient CreateClient(string apiKey)
    {
        var options = new OpenAIClientOptions();
        options.NetworkTimeout = TimeSpan.FromMinutes(10);
        return new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);
    }

    public OpenAIService(string apiKey, string model)
    {
        _client = CreateClient(apiKey);
        _chatClient = _client.GetChatClient(model);
    }

    public OpenAIService(string apiKey, AIModelConfig modelConfig, TokenUsageTracker? usageTracker = null)
    {
        _modelConfig = modelConfig;
        _usageTracker = usageTracker;
        _client = CreateClient(apiKey);
        _chatClient = _client.GetChatClient(modelConfig.ModelId);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
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
        catch
        {
            return false;
        }
    }

    public async Task<string> GetCompletionAsync(
        string systemPrompt,
        string userPrompt,
        string? operationName = null,
        CancellationToken ct = default)
    {
        Exception? lastException = null;
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
                    TrackUsage(operationName ?? "Completion", response.Value.Usage);

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

        throw new InvalidOperationException(
            $"Failed to get response from OpenAI after multiple attempts. {lastException?.Message}",
            lastException);
    }

    public async Task<T?> GetJsonCompletionAsync<T>(
        string systemPrompt,
        string userPrompt,
        string? operationName = null,
        CancellationToken ct = default) where T : class
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt + "\n\nIMPORTANT: Respond ONLY with valid JSON. No markdown, no explanations, just the JSON object."),
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
                    TrackUsage(operationName ?? "JSON Completion", response.Value.Usage);

                    var json = response.Value.Content[0].Text;
                    return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
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

        throw new InvalidOperationException(
            $"Failed to get valid JSON response from OpenAI after multiple attempts. {lastException?.Message}",
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
                return null;
            }
            catch (Exception ex) when (IsRetryable(ex, ct))
            {
                await DelayForRetryAsync(attempt, ex, ct);
            }
        }

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
                return null;
            }
            catch (Exception ex) when (IsRetryable(ex, ct))
            {
                await DelayForRetryAsync(attempt, ex, ct);
            }
        }

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

    private static TimeSpan GetRetryDelay(int attempt, Exception ex)
    {
        if (ex is ClientResultException { Status: 429 } && RateLimitDelayOverride.HasValue)
            return RateLimitDelayOverride.Value;

        var baseSeconds = Math.Pow(2, attempt + 1);
        var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromSeconds(baseSeconds * jitter);
    }

    private static Task DelayForRetryAsync(int attempt, Exception ex, CancellationToken ct)
    {
        return Task.Delay(GetRetryDelay(attempt, ex), ct);
    }
}
