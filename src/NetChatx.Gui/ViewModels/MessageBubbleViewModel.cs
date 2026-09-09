using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetChatx.Gui.Helpers;
using NetChatx.Storage.Models;

namespace NetChatx.Gui.ViewModels;

public sealed partial class MessageBubbleViewModel : ViewModelBase
{
    private static readonly Regex ImageUrlRegex = new(
        @"https?://[^\s<>""]+?\.(?:png|jpe?g|gif|webp|bmp)(?:\?[^\s<>""]*)?|file:///[^\s<>""]+?\.(?:png|jpe?g|gif|webp|bmp)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReceiptIcon))]
    private MessageDirection _direction = MessageDirection.Outbound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedTime))]
    [NotifyPropertyChangedFor(nameof(FormattedDateTime))]
    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private string _senderName = "Me";

    [ObservableProperty]
    private bool _isEncrypted;

    [ObservableProperty]
    private string? _encryptionType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReceiptIcon))]
    private bool _isRead;

    [ObservableProperty]
    private string? _stanzaId;

    [ObservableProperty]
    private bool _showDateHeader;

    [ObservableProperty]
    private string? _dateHeader;

    [ObservableProperty]
    private string? _imageUrl;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private bool _isOnlyImage;

    [ObservableProperty]
    private Bitmap? _imageThumbnail;

    [ObservableProperty]
    private bool _isLoadingImage;

    public string FormattedTime
    {
        get
        {
            var local = Timestamp.ToLocalTime();
            var today = DateTime.Today;

            if (local.Date == today)
            {
                return local.ToString("HH:mm");
            }
            else if (local.Date == today.AddDays(-1))
            {
                return $"Yesterday {local:HH:mm}";
            }
            else if (local.Year == today.Year)
            {
                return local.ToString("MMM d, HH:mm");
            }
            else
            {
                return local.ToString("yyyy-MM-dd HH:mm");
            }
        }
    }

    public string FormattedDateTime => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string ReceiptIcon => Direction == MessageDirection.Outbound ? (IsRead ? "✓✓" : "✓") : string.Empty;

    public void ExtractImageUrl(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            HasImage = false;
            ImageUrl = null;
            IsOnlyImage = false;
            return;
        }

        var match = ImageUrlRegex.Match(body);
        if (match.Success)
        {
            ImageUrl = match.Value;
            HasImage = true;
            IsOnlyImage = body.Trim().Equals(match.Value, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            HasImage = false;
            ImageUrl = null;
            IsOnlyImage = false;
        }
    }

    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(ImageUrl) || ImageThumbnail is not null) return;
        IsLoadingImage = true;
        try
        {
            var bmp = await AsyncImageLoader.LoadImageAsync(ImageUrl);
            if (bmp is not null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ImageThumbnail = bmp;
                    IsLoadingImage = false;
                });
            }
            else
            {
                Dispatcher.UIThread.Post(() => IsLoadingImage = false);
            }
        }
        catch
        {
            Dispatcher.UIThread.Post(() => IsLoadingImage = false);
        }
    }

    [RelayCommand]
    public void OpenImage()
    {
        if (string.IsNullOrEmpty(ImageUrl)) return;
        if (!Uri.TryCreate(ImageUrl, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

        try
        {
            Process.Start(new ProcessStartInfo(ImageUrl) { UseShellExecute = true });
        }
        catch
        {
            // Soft failure
        }
    }

    public static string FormatDateHeader(DateTimeOffset dto)
    {
        var local = dto.ToLocalTime();
        var today = DateTime.Today;

        if (local.Date == today)
            return "Today";
        if (local.Date == today.AddDays(-1))
            return "Yesterday";
        if (local.Year == today.Year)
            return local.ToString("MMMM d");

        return local.ToString("MMMM d, yyyy");
    }

    public static MessageBubbleViewModel FromChatMessage(ChatMessage msg)
    {
        var vm = new MessageBubbleViewModel
        {
            Id = msg.Id,
            Body = msg.Body,
            Direction = msg.Direction,
            Timestamp = msg.Timestamp,
            SenderName = msg.Direction == MessageDirection.Outbound ? "Me" : msg.SenderJid,
            IsEncrypted = msg.IsEncrypted,
            EncryptionType = msg.EncryptionType,
            IsRead = msg.IsRead,
            StanzaId = msg.StanzaId
        };

        vm.ExtractImageUrl(msg.Body);
        if (vm.HasImage)
        {
            _ = vm.LoadThumbnailAsync();
        }

        return vm;
    }
}
