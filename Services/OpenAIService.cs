using System.ClientModel;
using System.Text.Json;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Images;
using ReelDiscovery.Models;

namespace ReelDiscovery.Services;

public class OpenAIService
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly OpenAIClient _client;
    private readonly ChatClient _chatClient;
    private readonly AIModelConfig? _modelConfig;
    private readonly TokenUsageTracker? _usageTracker;
    private const int MaxRetries = 3;

    public OpenAIService(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
        _client = new OpenAIClient(apiKey);
        _chatClient = _client.GetChatClient(model);
    }

    public OpenAIService(string apiKey, AIModelConfig modelConfig, TokenUsageTracker? usageTracker = null)
    {
        _apiKey = apiKey;
        _model = modelConfig.ModelId;
        _modelConfig = modelConfig;
        _usageTracker = usageTracker;
        _client = new OpenAIClient(apiKey);
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
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userPrompt)
                };

                var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: ct);

                if (response.Value.Content.Count > 0)
                {
                    // Track token usage if configured
                    TrackUsage(operationName ?? "Completion", response.Value.Usage);

                    return response.Value.Content[0].Text;
                }

                throw new InvalidOperationException("Empty response from OpenAI");
            }
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                // Rate limited - exponential backoff
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                await Task.Delay(delay, ct);
            }
            catch (ClientResultException ex) when (ex.Status == 503)
            {
                // Service unavailable - retry
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        throw new InvalidOperationException("Failed to get response from OpenAI after multiple attempts.");
    }

    public async Task<T?> GetJsonCompletionAsync<T>(
        string systemPrompt,
        string userPrompt,
        string? operationName = null,
        CancellationToken ct = default) where T : class
    {
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
                    ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
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
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                await Task.Delay(delay, ct);
            }
            catch (ClientResultException ex) when (ex.Status == 503)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (JsonException)
            {
                // JSON parsing failed, retry
                if (attempt == MaxRetries - 1)
                    throw;
            }
        }

        throw new InvalidOperationException("Failed to get valid JSON response from OpenAI after multiple attempts.");
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
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                await Task.Delay(delay, ct);
            }
            catch (ClientResultException ex) when (ex.Status == 503)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (ClientResultException ex) when (ex.Status == 400)
            {
                // Content policy violation or invalid prompt - don't retry
                return null;
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
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                await Task.Delay(delay, ct);
            }
            catch (ClientResultException ex) when (ex.Status == 503)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (ClientResultException ex) when (ex.Status == 400)
            {
                // Invalid request - don't retry
                return null;
            }
        }

        return null;
    }
}
