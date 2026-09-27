using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Stanza.Gui.Helpers;
using Stanza.Gui.Services;
using Stanza.Storage.Models;

namespace Stanza.Gui.ViewModels;

public sealed partial class AccountProfileViewModel : ObservableObject
{
    public AccountProfile Profile { get; }
    public AccountSession? Session { get; }

    [ObservableProperty]
    private string _jid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(Initials))]
    private string? _label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountBrush))]
    private string? _colorHex;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _statusDisplay = "Disconnected";

    [ObservableProperty]
    private IBrush _statusBrush = Brushes.Gray;

    public string DisplayName => !string.IsNullOrWhiteSpace(Label) ? Label : Jid;

    public string Initials => AvatarHelper.GetInitials(DisplayName);

    public IBrush AvatarBackgroundBrush => AvatarHelper.GetAvatarColorBrush(DisplayName);

    public IBrush AccountBrush
    {
        get
        {
            if (!string.IsNullOrEmpty(ColorHex) && Color.TryParse(ColorHex, out var c))
            {
                return new SolidColorBrush(c);
            }
            return new SolidColorBrush(Color.Parse("#00F0FF"));
        }
    }

    public AccountProfileViewModel(AccountProfile profile, AccountSession? session = null)
    {
        Profile = profile;
        Session = session;
        _jid = profile.Jid;
        _label = profile.Label;
        _colorHex = profile.ColorHex;
        _isActive = profile.IsActive;

        UpdateStatusFromSession();

        if (session is not null)
        {
            session.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AccountSession.ConnectionState))
                {
                    UpdateStatusFromSession();
                }
            };
        }
    }

    public void UpdateStatusFromSession()
    {
        if (!IsActive)
        {
            StatusDisplay = LocalizationManager.Instance.GetString("Settings_Accounts_Disconnected");
            StatusBrush = Brushes.Gray;
            return;
        }

        if (Session is null)
        {
            StatusDisplay = LocalizationManager.Instance.GetString("Settings_Accounts_Disconnected");
            StatusBrush = Brushes.Gray;
            return;
        }

        switch (Session.ConnectionState)
        {
            case AccountConnectionState.Connected:
                StatusDisplay = LocalizationManager.Instance.GetString("Settings_Accounts_Connected");
                StatusBrush = Brushes.LimeGreen;
                break;
            case AccountConnectionState.Connecting:
            case AccountConnectionState.Reconnecting:
                StatusDisplay = LocalizationManager.Instance.GetString("Settings_Accounts_Connecting");
                StatusBrush = Brushes.Goldenrod;
                break;
            case AccountConnectionState.Error:
                StatusDisplay = !string.IsNullOrEmpty(Session.LastErrorMessage)
                    ? string.Format(LocalizationManager.Instance.GetString("Settings_Accounts_Status_Error"), Session.LastErrorMessage)
                    : LocalizationManager.Instance.GetString("Settings_Accounts_Error");
                StatusBrush = Brushes.Crimson;
                break;
            default:
                StatusDisplay = LocalizationManager.Instance.GetString("Settings_Accounts_Disconnected");
                StatusBrush = Brushes.Gray;
                break;
        }
    }
}
