using CommunityToolkit.Mvvm.ComponentModel;
using Stanza.Storage.Models;

namespace Stanza.Gui.ViewModels;

public sealed partial class ContactItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _accountJid = string.Empty;

    [ObservableProperty]
    private string _contactJid = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(Initials))]
    private string? _name;

    [ObservableProperty]
    private string _presenceShow = "offline"; // available, away, dnd, offline

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _subscription = "none";

    [ObservableProperty]
    private int _unreadCount;

    [ObservableProperty]
    private string? _lastMessagePreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Avalonia.Media.Imaging.Bitmap? _avatar;

    [ObservableProperty]
    private string? _avatarHash;

    public bool HasAvatar => Avatar is not null;

    public string Initials => Helpers.AvatarHelper.GetInitials(DisplayName);

    public Avalonia.Media.IBrush AvatarBackgroundBrush => Helpers.AvatarHelper.GetAvatarColorBrush(ContactJid);

    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : ContactJid;

    public static ContactItemViewModel FromRosterContact(RosterContact contact)
    {
        return new ContactItemViewModel
        {
            AccountJid = contact.AccountJid,
            ContactJid = contact.ContactJid,
            Name = contact.Name,
            Subscription = contact.Subscription,
            PresenceShow = !string.IsNullOrWhiteSpace(contact.PresenceShow) ? contact.PresenceShow : "offline",
            StatusMessage = contact.PresenceStatus
        };
    }
}
