namespace Reef.Core.Services;

/// <summary>
/// Service for displaying the password change modal at the layout level
/// </summary>
public class PasswordChangeModalService
{
    public bool IsVisible { get; private set; }
    public string? Username { get; private set; }
    public bool IsRequired { get; private set; }

    public event Action? OnShow;
    public event Action? OnHide;

    /// <summary>
    /// Show the password change modal
    /// </summary>
    /// <param name="username">The username of the current user</param>
    /// <param name="isRequired">If true, the modal cannot be dismissed without changing password</param>
    public void Show(string username, bool isRequired = false)
    {
        Username = username;
        IsRequired = isRequired;
        IsVisible = true;
        OnShow?.Invoke();
    }

    /// <summary>
    /// Hide the password change modal
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
        Username = null;
        IsRequired = false;
        OnHide?.Invoke();
    }
}
