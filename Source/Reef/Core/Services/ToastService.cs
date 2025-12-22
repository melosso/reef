namespace Reef.Core.Services;

/// <summary>
/// Service for displaying toast notifications in Blazor components
/// </summary>
public class ToastService
{
    private readonly List<ToastMessage> _toasts = new();
    public IReadOnlyList<ToastMessage> Toasts => _toasts.AsReadOnly();

    public event Action? OnToastAdded;
    public event Action? OnToastRemoved;

    /// <summary>
    /// Shows a success toast notification
    /// </summary>
    public void ShowSuccess(string message, int durationMs = 5000)
    {
        ShowToast(message, ToastType.Success, durationMs);
    }

    /// <summary>
    /// Shows an error toast notification
    /// </summary>
    public void ShowError(string message, int durationMs = 7000)
    {
        ShowToast(message, ToastType.Error, durationMs);
    }

    /// <summary>
    /// Shows a warning toast notification
    /// </summary>
    public void ShowWarning(string message, int durationMs = 6000)
    {
        ShowToast(message, ToastType.Warning, durationMs);
    }

    /// <summary>
    /// Shows an info toast notification
    /// </summary>
    public void ShowInfo(string message, int durationMs = 5000)
    {
        ShowToast(message, ToastType.Info, durationMs);
    }

    private void ShowToast(string message, ToastType type, int durationMs)
    {
        var toast = new ToastMessage
        {
            Id = Guid.NewGuid(),
            Message = message,
            Type = type,
            CreatedAt = DateTime.UtcNow
        };

        _toasts.Add(toast);
        OnToastAdded?.Invoke();

        // Auto-remove after duration
        Task.Delay(durationMs).ContinueWith(_ =>
        {
            RemoveToast(toast.Id);
        });
    }

    public void RemoveToast(Guid id)
    {
        var toast = _toasts.FirstOrDefault(t => t.Id == id);
        if (toast != null)
        {
            _toasts.Remove(toast);
            OnToastRemoved?.Invoke();
        }
    }
}

public class ToastMessage
{
    public Guid Id { get; set; }
    public string Message { get; set; } = string.Empty;
    public ToastType Type { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ToastType
{
    Success,
    Error,
    Warning,
    Info
}
