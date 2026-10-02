using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Stanza.Gui.ViewModels;
using Stanza.Storage.Export;

namespace Stanza.Gui.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel vm)
        {
            vm.RequestExportAllCallback = HandleExportAllAsync;
            vm.RequestImportBackupCallback = HandleImportBackupAsync;
        }
    }

    private async Task HandleExportAllAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is null) return;
            if (DataContext is not SettingsViewModel vm) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export All Messages",
                DefaultExtension = "json",
                SuggestedFileName = $"stanza-messages-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                FileTypeChoices =
                [
                    new FilePickerFileType("JSON Backup (*.json)") { Patterns = ["*.json"] },
                    new FilePickerFileType("HTML Document (*.html)") { Patterns = ["*.html"] },
                    new FilePickerFileType("Plain Text (*.txt)") { Patterns = ["*.txt"] }
                ]
            });

            if (file is null) return;

            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            var format = extension switch
            {
                ".html" or ".htm" => MessageExportFormat.Html,
                ".txt" => MessageExportFormat.PlainText,
                _ => MessageExportFormat.Json
            };

            await using var stream = await file.OpenWriteAsync();
            await vm.ExportAllMessagesAsync(stream, format);
        }
        catch (Exception ex)
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.BackupStatusMessage = $"Export failed: {ex.Message}";
            }
        }
    }

    private async Task HandleImportBackupAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is null) return;
            if (DataContext is not SettingsViewModel vm) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Message Backup",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("JSON Backup (*.json)") { Patterns = ["*.json"] }
                ]
            });

            if (files.Count == 0) return;

            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            await vm.ImportBackupAsync(stream);
        }
        catch (Exception ex)
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.BackupStatusMessage = $"Import failed: {ex.Message}";
            }
        }
    }

    private async void OnChooseAvatarClicked(object? sender, RoutedEventArgs e)
    {
        try
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
        catch
        {
            // Soft failure / prevent unhandled exception in async void event handler
        }
    }
}
