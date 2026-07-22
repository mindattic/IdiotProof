namespace IdiotProof.Blazor.Services;

public enum ToastKind { Info, Success, Warning, Error }

public sealed record ToastMessage(Guid Id, string Body, ToastKind Kind);

/// <summary>
/// App-wide toast notification bus. Inject as scoped; ToastContainer subscribes
/// via Changed and re-renders. Auto-dismisses each toast after 4.5 seconds.
/// </summary>
public sealed class ToastService
{
    private readonly List<ToastMessage> toasts = [];
    public IReadOnlyList<ToastMessage> Toasts => toasts;
    public event Action? Changed;

    public void Show(string body, ToastKind kind = ToastKind.Info)
    {
        var msg = new ToastMessage(Guid.NewGuid(), body, kind);
        toasts.Add(msg);
        Changed?.Invoke();
        _ = AutoDismissAsync(msg.Id);
    }

    public void Dismiss(Guid id)
    {
        toasts.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }

    private async Task AutoDismissAsync(Guid id)
    {
        await Task.Delay(4500);
        Dismiss(id);
    }
}
