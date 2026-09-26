using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Stanza.Gui.Helpers;
using Stanza.Gui.ViewModels;

namespace Stanza.Gui.Views;

public partial class MainChatView : UserControl
{
    private ChatConversationViewModel? _currentConversation;
    private ScrollViewer? _messagesScrollViewer;
    private TextBox? _messageInputBox;
    private TextBox? _searchInputBox;
    private Button? _attachFileButton;
    private Button? _insertCodeBlockButton;
    private GridSplitter? _sidebarSplitter;
    private Control? _sidebarGrid;
    private double _savedSidebarWidth = 300;
    private bool _wasNearBottom = true;

    public MainChatView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _messagesScrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer");
        _messageInputBox = this.FindControl<TextBox>("MessageInputBox");
        _searchInputBox = this.FindControl<TextBox>("SearchInputBox");
        _attachFileButton = this.FindControl<Button>("AttachFileButton");
        _insertCodeBlockButton = this.FindControl<Button>("InsertCodeBlockButton");
        _sidebarSplitter = this.FindControl<GridSplitter>("SidebarSplitter");
        _sidebarGrid = this.FindControl<Control>("SidebarGrid");

        if (_messageInputBox is not null)
        {
            _messageInputBox.AddHandler(InputElement.KeyDownEvent, OnMessageInputKeyDown, RoutingStrategies.Tunnel);
            _messageInputBox.AddHandler(InputElement.TextInputEvent, OnMessageInputTextInput, RoutingStrategies.Tunnel);
        }

        if (_searchInputBox is not null)
        {
            _searchInputBox.AddHandler(InputElement.KeyDownEvent, OnSearchInputKeyDown, RoutingStrategies.Tunnel);
        }

        AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        if (_insertCodeBlockButton is not null)
        {
            _insertCodeBlockButton.Click += OnInsertCodeBlockButtonClick;
        }

        if (_attachFileButton is not null)
        {
            _attachFileButton.Click += OnAttachFileButtonClick;
        }

        if (_messagesScrollViewer is not null)
        {
            _messagesScrollViewer.SizeChanged += OnMessagesScrollViewerSizeChanged;
            _messagesScrollViewer.PropertyChanged += OnMessagesScrollViewerPropertyChanged;
        }

        if (DataContext is MainChatViewModel vm)
        {
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            vm.CodeBlockInjected += OnCodeBlockInjected;
            UpdateActiveConversation(vm.ActiveConversation);
            ApplySidebarState(vm.IsSidebarOpen);

            if (TopLevel.GetTopLevel(this) is Window window)
            {
                vm.NotificationService?.AttachWindow(window);
            }
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (DataContext is MainChatViewModel vm)
        {
            vm.PropertyChanged -= OnMainViewModelPropertyChanged;
            vm.CodeBlockInjected -= OnCodeBlockInjected;
            vm.NotificationService?.DetachWindow();
        }

        if (_messageInputBox is not null)
        {
            _messageInputBox.RemoveHandler(InputElement.KeyDownEvent, OnMessageInputKeyDown);
            _messageInputBox.RemoveHandler(InputElement.TextInputEvent, OnMessageInputTextInput);
        }

        if (_searchInputBox is not null)
        {
            _searchInputBox.RemoveHandler(InputElement.KeyDownEvent, OnSearchInputKeyDown);
        }

        RemoveHandler(InputElement.KeyDownEvent, OnGlobalKeyDown);
        RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        RemoveHandler(DragDrop.DropEvent, OnDrop);

        if (_insertCodeBlockButton is not null)
        {
            _insertCodeBlockButton.Click -= OnInsertCodeBlockButtonClick;
        }

        if (_attachFileButton is not null)
        {
            _attachFileButton.Click -= OnAttachFileButtonClick;
        }

        if (_messagesScrollViewer is not null)
        {
            _messagesScrollViewer.SizeChanged -= OnMessagesScrollViewerSizeChanged;
            _messagesScrollViewer.PropertyChanged -= OnMessagesScrollViewerPropertyChanged;
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

    private double _preLoadExtentHeight;
    private double _preLoadOffsetY;
    private bool _isPreservingScroll;

    private void UpdateActiveConversation(ChatConversationViewModel? newConversation)
    {
        if (_currentConversation == newConversation) return;

        if (_currentConversation is not null)
        {
            _currentConversation.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            _currentConversation.ScrollToBottomRequested -= ScrollToLatestMessage;
            _currentConversation.EditStarted -= OnConversationEditStarted;
            _currentConversation.OlderHistoryLoading -= OnOlderHistoryLoading;
            _currentConversation.OlderHistoryLoaded -= OnOlderHistoryLoaded;
        }

        _currentConversation = newConversation;

        if (_currentConversation is not null)
        {
            _currentConversation.Messages.CollectionChanged += OnMessagesCollectionChanged;
            _currentConversation.ScrollToBottomRequested += ScrollToLatestMessage;
            _currentConversation.EditStarted += OnConversationEditStarted;
            _currentConversation.OlderHistoryLoading += OnOlderHistoryLoading;
            _currentConversation.OlderHistoryLoaded += OnOlderHistoryLoaded;
            ScrollToLatestMessage();
            _messageInputBox?.Focus();
        }
    }

    private void OnOlderHistoryLoading()
    {
        _messagesScrollViewer ??= this.FindControl<ScrollViewer>("MessagesScrollViewer");
        if (_messagesScrollViewer is null) return;

        _isPreservingScroll = true;
        _wasNearBottom = false;
        _preLoadExtentHeight = _messagesScrollViewer.Extent.Height;
        _preLoadOffsetY = _messagesScrollViewer.Offset.Y;
    }

    private void OnOlderHistoryLoaded()
    {
        _messagesScrollViewer ??= this.FindControl<ScrollViewer>("MessagesScrollViewer");
        if (_messagesScrollViewer is null)
        {
            _isPreservingScroll = false;
            return;
        }

        // Wait for layout pass at Loaded priority to measure newly inserted items
        Dispatcher.UIThread.Post(() =>
        {
            if (_messagesScrollViewer is null)
            {
                _isPreservingScroll = false;
                return;
            }

            var newExtentHeight = _messagesScrollViewer.Extent.Height;
            var heightDelta = newExtentHeight - _preLoadExtentHeight;

            if (heightDelta > 0)
            {
                var targetOffset = _preLoadOffsetY + heightDelta;
                _messagesScrollViewer.Offset = new Vector(_messagesScrollViewer.Offset.X, targetOffset);
            }

            _wasNearBottom = false;

            // Wait one more layout cycle before re-enabling normal auto-scroll
            Dispatcher.UIThread.Post(() =>
            {
                _isPreservingScroll = false;
                _wasNearBottom = IsNearBottom(200.0);
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
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

    private void OnMessagesScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty)
        {
            _wasNearBottom = IsNearBottom(200.0);
        }
        else if (e.Property == ScrollViewer.ExtentProperty)
        {
            if (_currentConversation is null) return;
            if (_currentConversation.IsLoadingOlderHistory || _currentConversation.IsLoadingHistory || _currentConversation.IsSyncing || _isPreservingScroll)
            {
                return;
            }

            if (_wasNearBottom)
            {
                ScrollToLatestMessage();
            }
        }
    }

    private bool IsNearBottom(double threshold = 200.0)
    {
        _messagesScrollViewer ??= this.FindControl<ScrollViewer>("MessagesScrollViewer");
        if (_messagesScrollViewer is null) return true;
        var maxScroll = Math.Max(0, _messagesScrollViewer.Extent.Height - _messagesScrollViewer.Viewport.Height);
        return _messagesScrollViewer.Offset.Y >= (maxScroll - threshold);
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Don't auto-scroll to bottom if loading older history, loading initial history, syncing, or preserving scroll
        if (_currentConversation is null) return;
        if (_currentConversation.IsLoadingOlderHistory || _currentConversation.IsLoadingHistory || _currentConversation.IsSyncing || _isPreservingScroll)
        {
            return;
        }

        if (e.Action is NotifyCollectionChangedAction.Add)
        {
            var newItems = e.NewItems?.OfType<MessageBubbleViewModel>().ToList();
            var isOutbound = newItems?.Any(m => m.IsOutbound) == true;
            var isAddedAtEnd = e.NewStartingIndex >= _currentConversation.Messages.Count - (newItems?.Count ?? 1);

            if ((isOutbound && isAddedAtEnd) || IsNearBottom())
            {
                ScrollToLatestMessage();
            }
        }
        else if (e.Action is NotifyCollectionChangedAction.Reset)
        {
            ScrollToLatestMessage();
        }
    }

    private void OnMessagesScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_currentConversation is not null &&
            !_currentConversation.IsLoadingOlderHistory &&
            !_currentConversation.IsLoadingHistory &&
            !_isPreservingScroll &&
            IsNearBottom())
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

            // Escape key: Cancel code block editor if open, cancel message editing if active, or discard pending image preview
            if (e.Key == Key.Escape)
            {
                if (mainVm.Help.IsOpen)
                {
                    mainVm.CloseHelp();
                    e.Handled = true;
                    return;
                }

                if (mainVm.About.IsOpen)
                {
                    mainVm.CloseAbout();
                    e.Handled = true;
                    return;
                }

                if (mainVm.CodeBlockEditor.IsOpen)
                {
                    mainVm.CodeBlockEditor.CancelCommand.Execute(null);
                    e.Handled = true;
                    return;
                }

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

                if (conv.HasReplyingMessage)
                {
                    conv.CancelReplyingMessage();
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
                var sendOnEnter = mainVm.SendOnEnter;
                var isCtrlOrMeta = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
                var isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

                var shouldSend = sendOnEnter ? (!isShift && !isCtrlOrMeta) : isCtrlOrMeta;

                if (!shouldSend)
                {
                    // Allow normal newline insertion
                    return;
                }

                // Check for code block trigger or send message
                textBox = sender as TextBox;
                var currentText = textBox?.Text ?? conv.InputText;

                // If user typed ``` or ```<lang> and pressed Enter, open the Code Block Editor
                var trimmed = currentText.Trim();
                if (trimmed.StartsWith("```") && !trimmed.Contains('\n') && !trimmed.Contains('\r'))
                {
                    e.Handled = true;
                    var langHint = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;

                    if (textBox is not null)
                    {
                        textBox.Text = string.Empty;
                    }
                    conv.InputText = string.Empty;

                    mainVm.OpenCodeBlockEditor(initialCode: string.Empty, languageHint: langHint, insertionIndex: 0);
                    return;
                }

                var hasText = !string.IsNullOrWhiteSpace(currentText);
                var hasPendingImage = conv.HasPendingImage;

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

    private void OnMessageInputTextInput(object? sender, TextInputEventArgs e)
    {
        try
        {
            if (DataContext is not MainChatViewModel mainVm || mainVm.ActiveConversation is null) return;
            if (_messageInputBox is null) return;

            var inputText = e.Text;
            if (string.IsNullOrEmpty(inputText)) return;

            // Check if typing a single backtick that completes 3 consecutive backticks
            if (inputText == "`")
            {
                var caret = _messageInputBox.CaretIndex;
                var current = _messageInputBox.Text ?? string.Empty;

                // Check if the preceding 2 characters before caret are "``"
                if (caret >= 2 && current.Length >= 2 &&
                    current[caret - 1] == '`' && current[caret - 2] == '`')
                {
                    // Check if 4th backtick (already part of a larger sequence)
                    if (caret >= 3 && current[caret - 3] == '`')
                    {
                        return;
                    }

                    e.Handled = true;

                    // Remove the two preceding backticks from the input box
                    var updated = current.Remove(caret - 2, 2);
                    _messageInputBox.Text = updated;
                    _messageInputBox.CaretIndex = caret - 2;
                    mainVm.ActiveConversation.InputText = updated;

                    // Open Code Block Editor!
                    mainVm.OpenCodeBlockEditor(initialCode: string.Empty, insertionIndex: caret - 2);
                    return;
                }
            }
            else if (inputText == "```")
            {
                e.Handled = true;
                var caret = _messageInputBox.CaretIndex;
                mainVm.OpenCodeBlockEditor(initialCode: string.Empty, insertionIndex: caret);
                return;
            }
        }
        catch
        {
            // Soft failure
        }
    }

    private void OnInsertCodeBlockButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainChatViewModel mainVm || mainVm.ActiveConversation is null) return;

        var selectedText = _messageInputBox?.SelectedText ?? string.Empty;
        var caretIndex = _messageInputBox?.CaretIndex ?? -1;

        mainVm.OpenCodeBlockEditor(initialCode: selectedText, insertionIndex: caretIndex);
    }

    private void OnCodeBlockEditorBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainChatViewModel mainVm)
        {
            mainVm.CodeBlockEditor.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCodeBlockInjected()
    {
        if (_messageInputBox is not null && DataContext is MainChatViewModel vm && vm.ActiveConversation is not null)
        {
            _messageInputBox.Text = vm.ActiveConversation.InputText;
            _messageInputBox.CaretIndex = _messageInputBox.Text?.Length ?? 0;
            _messageInputBox.Focus();
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
            var result = await ClipboardImageHelper.GetClipboardImageAsync(topLevel);
            if (result is not null && result.Bytes.Length > 0)
            {
                conv.StageImageAttachment(result.Bytes, result.FileName);
                ScrollToLatestMessage();
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
                ScrollToLatestMessage();
            }
        }
        catch
        {
            // Soft failure / prevent unhandled exception in async void event handler
        }
    }

    private async void OnSearchInputKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key is Key.Enter or Key.Return && DataContext is MainChatViewModel vm)
            {
                await vm.ExecuteSearchAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && DataContext is MainChatViewModel mainVm)
            {
                mainVm.CloseSearch();
                e.Handled = true;
            }
        }
        catch
        {
            // Soft failure / prevent unhandled exception in async void event handler
        }
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1 && DataContext is MainChatViewModel vmF1)
        {
            vmF1.OpenHelp();
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            _searchInputBox?.Focus();
            _searchInputBox?.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && DataContext is MainChatViewModel vm)
        {
            if (vm.Settings.IsOpen)
            {
                vm.Settings.Close();
                e.Handled = true;
            }
            else if (vm.Help.IsOpen)
            {
                vm.CloseHelp();
                e.Handled = true;
            }
            else if (vm.About.IsOpen)
            {
                vm.CloseAbout();
                e.Handled = true;
            }
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files) || _currentConversation is null) return;

        var files = e.Data.GetFiles();
        if (files is null) return;

        foreach (var file in files)
        {
            var localPath = file.TryGetLocalPath() ?? file.Path.LocalPath;
            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath) && ClipboardImageHelper.IsSupportedImageFile(localPath))
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(localPath);
                    _currentConversation.StageImageAttachment(bytes, file.Name);
                    ScrollToLatestMessage();
                    break;
                }
                catch
                {
                    // Soft failure loading dragged file
                }
            }
        }
    }

    public void OnNewChatBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainChatViewModel vm)
        {
            vm.CancelNewChatDialog();
            e.Handled = true;
        }
    }

    public void OnSettingsBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainChatViewModel vm)
        {
            vm.Settings.Close();
            e.Handled = true;
        }
    }

    public void OnHelpBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainChatViewModel vm)
        {
            vm.CloseHelp();
            e.Handled = true;
        }
    }

    public void OnAboutBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainChatViewModel vm)
        {
            vm.CloseAbout();
            e.Handled = true;
        }
    }

    public void ScrollToLatestMessage()
    {
        _wasNearBottom = true;
        Dispatcher.UIThread.Post(() =>
        {
            _messagesScrollViewer ??= this.FindControl<ScrollViewer>("MessagesScrollViewer");
            if (_messagesScrollViewer is null) return;

            _messagesScrollViewer.ScrollToEnd();

            // Run follow-up pass at Loaded priority to ensure newly measured layouts and images are accounted for
            Dispatcher.UIThread.Post(() =>
            {
                _messagesScrollViewer?.ScrollToEnd();
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);
    }
}
