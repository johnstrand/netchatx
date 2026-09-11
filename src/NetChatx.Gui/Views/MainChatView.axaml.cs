using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NetChatx.Gui.Helpers;
using NetChatx.Gui.ViewModels;

namespace NetChatx.Gui.Views;

public partial class MainChatView : UserControl
{
    private ChatConversationViewModel? _currentConversation;
    private ScrollViewer? _messagesScrollViewer;
    private TextBox? _messageInputBox;
    private Button? _attachFileButton;
    private GridSplitter? _sidebarSplitter;
    private Control? _sidebarGrid;
    private double _savedSidebarWidth = 300;

    public MainChatView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _messagesScrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer");
        _messageInputBox = this.FindControl<TextBox>("MessageInputBox");
        _attachFileButton = this.FindControl<Button>("AttachFileButton");
        _sidebarSplitter = this.FindControl<GridSplitter>("SidebarSplitter");
        _sidebarGrid = this.FindControl<Control>("SidebarGrid");

        if (_messageInputBox is not null)
        {
            _messageInputBox.AddHandler(InputElement.KeyDownEvent, OnMessageInputKeyDown, RoutingStrategies.Tunnel);
        }

        if (_attachFileButton is not null)
        {
            _attachFileButton.Click += OnAttachFileButtonClick;
        }

        if (_messagesScrollViewer is not null)
        {
            _messagesScrollViewer.SizeChanged += OnMessagesScrollViewerSizeChanged;
        }

        if (DataContext is MainChatViewModel vm)
        {
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            UpdateActiveConversation(vm.ActiveConversation);
            ApplySidebarState(vm.IsSidebarOpen);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_messageInputBox is not null)
        {
            _messageInputBox.RemoveHandler(InputElement.KeyDownEvent, OnMessageInputKeyDown);
        }

        if (_attachFileButton is not null)
        {
            _attachFileButton.Click -= OnAttachFileButtonClick;
        }

        if (_messagesScrollViewer is not null)
        {
            _messagesScrollViewer.SizeChanged -= OnMessagesScrollViewerSizeChanged;
        }

        if (DataContext is MainChatViewModel vm)
        {
            vm.PropertyChanged -= OnMainViewModelPropertyChanged;
        }

        UpdateActiveConversation(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainChatViewModel vm)
        {
            vm.PropertyChanged -= OnMainViewModelPropertyChanged;
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            UpdateActiveConversation(vm.ActiveConversation);
            ApplySidebarState(vm.IsSidebarOpen);
        }
        else
        {
            UpdateActiveConversation(null);
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainChatViewModel vm) return;

        if (e.PropertyName == nameof(MainChatViewModel.ActiveConversation))
        {
            UpdateActiveConversation(vm.ActiveConversation);
        }
        else if (e.PropertyName == nameof(MainChatViewModel.IsSidebarOpen))
        {
            ApplySidebarState(vm.IsSidebarOpen);
        }
    }

    private void ApplySidebarState(bool isOpen)
    {
        var rootGrid = this.FindControl<Grid>("RootChatGrid");
        if (rootGrid is null || rootGrid.ColumnDefinitions.Count < 2) return;

        var sidebarCol = rootGrid.ColumnDefinitions[0];
        var splitterCol = rootGrid.ColumnDefinitions[1];

        if (isOpen)
        {
            sidebarCol.MinWidth = 240;
            var targetWidth = _savedSidebarWidth >= 240 ? _savedSidebarWidth : 300;
            sidebarCol.Width = new GridLength(targetWidth, GridUnitType.Pixel);
            splitterCol.Width = new GridLength(2, GridUnitType.Pixel);

            if (_sidebarSplitter is not null)
            {
                _sidebarSplitter.IsVisible = true;
            }
            if (_sidebarGrid is not null)
            {
                _sidebarGrid.IsVisible = true;
            }
        }
        else
        {
            if (sidebarCol.ActualWidth > 0)
            {
                _savedSidebarWidth = sidebarCol.ActualWidth;
            }
            else if (sidebarCol.Width.IsAbsolute && sidebarCol.Width.Value >= 240)
            {
                _savedSidebarWidth = sidebarCol.Width.Value;
            }

            sidebarCol.MinWidth = 0;
            sidebarCol.Width = new GridLength(0, GridUnitType.Pixel);
            splitterCol.Width = new GridLength(0, GridUnitType.Pixel);

            if (_sidebarSplitter is not null)
            {
                _sidebarSplitter.IsVisible = false;
            }
            if (_sidebarGrid is not null)
            {
                _sidebarGrid.IsVisible = false;
            }
        }
    }

    private void UpdateActiveConversation(ChatConversationViewModel? newConversation)
    {
        if (_currentConversation == newConversation) return;

        if (_currentConversation is not null)
        {
            _currentConversation.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            _currentConversation.ScrollToBottomRequested -= ScrollToLatestMessage;
            _currentConversation.EditStarted -= OnConversationEditStarted;
        }

        _currentConversation = newConversation;

        if (_currentConversation is not null)
        {
            _currentConversation.Messages.CollectionChanged += OnMessagesCollectionChanged;
            _currentConversation.ScrollToBottomRequested += ScrollToLatestMessage;
            _currentConversation.EditStarted += OnConversationEditStarted;
            ScrollToLatestMessage();
            _messageInputBox?.Focus();
        }
    }

    private void OnConversationEditStarted()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_messageInputBox is not null)
            {
                _messageInputBox.Focus();
                _messageInputBox.CaretIndex = _messageInputBox.Text?.Length ?? 0;
            }
        });
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Don't auto-scroll to bottom if loading older history from the top
        if (_currentConversation?.IsLoadingOlderHistory == true) return;

        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            ScrollToLatestMessage();
        }
    }

    private void OnMessagesScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_currentConversation is not null && !_currentConversation.IsLoadingOlderHistory)
        {
            ScrollToLatestMessage();
        }
    }

    private async void OnMessageInputKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (DataContext is not MainChatViewModel mainVm ||
                mainVm.ActiveConversation is not { } conv)
            {
                return;
            }

            var textBox = sender as TextBox;

            // Escape key: Cancel message editing if active, or discard pending image preview
            if (e.Key == Key.Escape)
            {
                if (conv.IsEditingMessage)
                {
                    conv.CancelEditingMessage();
                    if (textBox is not null)
                    {
                        textBox.Text = conv.InputText;
                        textBox.CaretIndex = textBox.Text?.Length ?? 0;
                    }
                    e.Handled = true;
                    return;
                }

                if (conv.HasPendingImage)
                {
                    conv.ClearPendingImage();
                    e.Handled = true;
                    return;
                }
            }

            // Up arrow key: When input is empty and not already editing, edit the last sent outbound message
            if (e.Key == Key.Up && (string.IsNullOrEmpty(textBox?.Text) || string.IsNullOrEmpty(conv.InputText)) && !conv.IsEditingMessage)
            {
                conv.StartEditingLastSentMessage();
                if (conv.IsEditingMessage && textBox is not null)
                {
                    textBox.Text = conv.InputText;
                    textBox.CaretIndex = textBox.Text?.Length ?? 0;
                }
                e.Handled = true;
                return;
            }

            // Check for Image Paste (Ctrl+V or Cmd+V)
            if (e.Key == Key.V && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                if (await TryPasteImageAsync())
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key is Key.Enter or Key.Return)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    // Shift+Enter allows newline insertion
                    return;
                }

                // Enter without Shift: Send message (either text, pending image, or both)
                textBox = sender as TextBox;
                string currentText = textBox?.Text ?? conv.InputText;
                bool hasText = !string.IsNullOrWhiteSpace(currentText);
                bool hasPendingImage = conv.HasPendingImage;

                if (hasText || hasPendingImage)
                {
                    e.Handled = true;

                    if (textBox is not null && conv.InputText != textBox.Text)
                    {
                        conv.InputText = textBox.Text ?? string.Empty;
                    }

                    if (conv.SendMessageCommand.CanExecute(null))
                    {
                        await conv.SendMessageAsync();
                        if (textBox is not null)
                        {
                            textBox.Text = string.Empty;
                        }
                    }
                }
            }
        }
        catch
        {
            // Soft failure / prevent unhandled exception in async void event handler
        }
    }

    private async Task<bool> TryPasteImageAsync()
    {
        try
        {
            if (DataContext is not MainChatViewModel mainVm ||
                mainVm.ActiveConversation is not { } conv)
            {
                return false;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            var imageBytes = await ClipboardImageHelper.GetClipboardImageBytesAsync(topLevel);
            if (imageBytes is not null && imageBytes.Length > 0)
            {
                conv.StageImageAttachment(imageBytes);
                return true;
            }
        }
        catch
        {
            // Soft failure on image clipboard reading
        }

        return false;
    }

    private async void OnAttachFileButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainChatViewModel mainVm ||
                mainVm.ActiveConversation is not { } conv)
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Image to Send",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images (*.png, *.jpg, *.jpeg, *.gif, *.webp, *.bmp)")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp"]
                    }
                ]
            });

            if (files.Count > 0)
            {
                var file = files[0];
                await using var stream = await file.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                conv.StageImageAttachment(ms.ToArray(), file.Name);
            }
        }
        catch
        {
            // Soft failure / prevent unhandled exception in async void event handler
        }
    }

    public void ScrollToLatestMessage()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _messagesScrollViewer ??= this.FindControl<ScrollViewer>("MessagesScrollViewer");
            if (_messagesScrollViewer is null) return;

            _messagesScrollViewer.ScrollToEnd();

            // Additional pass after measure/render pass completes
            Dispatcher.UIThread.Post(() =>
            {
                _messagesScrollViewer?.ScrollToEnd();
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Normal);
    }
}
