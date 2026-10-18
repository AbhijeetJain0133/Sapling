using Sapling.Shared.Contracts;

namespace Sapling.Shared.Services;

/// <summary>
/// The Sapling AI conversation for this session. It lives in a scoped service rather than in the chat box so
/// the thread survives moving between pages, and so any page can open the box with a question already asked.
/// </summary>
public sealed class AssistantChat
{
    public List<AssistantMessageDto> Messages { get; } = [];

    public bool IsOpen { get; private set; }

    /// <summary>A question handed over by a page; the chat box sends it as soon as it sees it.</summary>
    public string? Pending { get; private set; }

    public event Action? Changed;

    public void Open(string? ask = null)
    {
        IsOpen = true;
        Pending = string.IsNullOrWhiteSpace(ask) ? null : ask.Trim();
        Changed?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        Changed?.Invoke();
    }

    public string? TakePending()
    {
        var ask = Pending;
        Pending = null;
        return ask;
    }

    public void Clear()
    {
        Messages.Clear();
        Changed?.Invoke();
    }
}
