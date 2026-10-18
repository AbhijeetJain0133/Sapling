using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sapling.Web.Ai;

/// <summary>Raised when the hosted model rejects the key or the account is out of credit, so the UI can say so.</summary>
public sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Asks the model for JSON and reads it back tolerantly: open models often wrap it in fences or prose.</summary>
public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    // Providers behind the router differ in what they accept. 0 = JSON mode + reasoning effort, 1 = reasoning
    // effort only, 2 = plain request. A 400 moves down a level, and the level is remembered for later calls.
    private static volatile int _compatibility;

    public static bool TryParse<T>(string? text, out T value)
    {
        value = default!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            trimmed = firstNewLine < 0 ? trimmed.Trim('`') : trimmed[(firstNewLine + 1)..];
            var fence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                trimmed = trimmed[..fence];
            }
        }

        if (TryDeserialize(trimmed, out value))
        {
            return true;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start && TryDeserialize(trimmed[start..(end + 1)], out value);
    }

    private static bool TryDeserialize<T>(string json, out T value)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(json, Options)!;
            return value is not null;
        }
        catch (JsonException)
        {
            value = default!;
            return false;
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public enum Reasoning
    {
        Low,
        Medium,
    }

    // The OpenAI SDK marks reasoning effort as experimental (OPENAI001). It is kept to this one spot, and a
    // provider that rejects it drops to a plain request (see _compatibility).
#pragma warning disable OPENAI001
    private static ChatCompletionOptions ReasoningOptions(Reasoning reasoning) => new()
    {
        ReasoningEffortLevel = reasoning == Reasoning.Low ? ChatReasoningEffortLevel.Low : ChatReasoningEffortLevel.Medium,
    };
#pragma warning restore OPENAI001

    /// <summary>
    /// Calls the model asking for JSON. <paramref name="reasoning"/> sets how long a reasoning model (gpt-oss)
    /// thinks first: low keeps the interviewer's replies quick, medium gives the report more care.
    /// </summary>
    public static async Task<string?> AskAsync(
        IChatClient chat, IList<ChatMessage> messages, float temperature, int maxOutputTokens,
        Reasoning reasoning, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var level = _compatibility;
                var options = new ChatOptions { Temperature = temperature, MaxOutputTokens = maxOutputTokens };
                if (level == 0)
                {
                    options.ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.Json;
                }

                if (level <= 1)
                {
                    options.RawRepresentationFactory = _ => ReasoningOptions(reasoning);
                }

                try
                {
                    return (await chat.GetResponseAsync(messages, options, ct)).Text;
                }
                catch (ClientResultException e) when (e.Status == 400 && level < 2)
                {
                    _compatibility = level + 1;
                }
            }
        }
        catch (ClientResultException e) when (e.Status is 401 or 402 or 403 or 429)
        {
            throw new AiUnavailableException(e.Status switch
            {
                401 or 403 => "The AI key was rejected. Check Ai:HuggingFace:ApiKey.",
                402 => "The Hugging Face account is out of credit.",
                _ => "The AI service is busy or over its limit. Try again in a minute.",
            }, e);
        }
    }
}
