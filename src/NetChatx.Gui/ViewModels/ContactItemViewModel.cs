using CommunityToolkit.Mvvm.ComponentModel;
using NetChatx.Storage.Models;

namespace NetChatx.Gui.ViewModels;

public sealed partial class ContactItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _accountJid = string.Empty;

    [ObservableProperty]
    private string _contactJid = string.Empty;

    [ObservableProperty]
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
