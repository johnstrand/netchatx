using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Stanza.Gui.ViewModels;

namespace Stanza.Gui.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnChooseAvatarClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Avatar Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image Files (*.png, *.jpg, *.jpeg, *.webp, *.gif, *.bmp)")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp"]
                }
            ]
        });

        if (files.Count > 0 && DataContext is SettingsViewModel vm)
        {
            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var name = file.Name;
            var mime = name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                ? "image/webp"
                : name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                ? "image/gif"
                : "image/png";

            await vm.SetAvatarBytesAsync(bytes, mime);
        }
    }
}
