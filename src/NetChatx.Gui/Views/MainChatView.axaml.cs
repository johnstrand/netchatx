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
    private TextBox? _searchInputBox;
    private Button? _attachFileButton;
    private Button? _insertCodeBlockButton;
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
        }

        if (DataContext is MainChatViewModel vm)
        {
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            vm.CodeBlockInjected += OnCodeBlockInjected;
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
        }

        if (DataContext is MainChatViewModel vm)
        {
            vm.PropertyChanged -= OnMainViewModelPropertyChanged;
            vm.CodeBlockInjected -= OnCodeBlockInjected;
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

            // Escape key: Cancel code block editor if open, cancel message editing if active, or discard pending image preview
            if (e.Key == Key.Escape)
            {
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
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    // Shift+Enter allows newline insertion
                    return;
                }

                // Enter without Shift: Check for code block trigger or send message
                textBox = sender as TextBox;
                string currentText = textBox?.Text ?? conv.InputText;

                // If user typed ``` or ```<lang> and pressed Enter, open the Code Block Editor
                string trimmed = currentText.Trim();
                if (trimmed.StartsWith("```") && !trimmed.Contains('\n') && !trimmed.Contains('\r'))
                {
                    e.Handled = true;
                    string langHint = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;

                    if (textBox is not null)
                    {
                        textBox.Text = string.Empty;
                    }
                    conv.InputText = string.Empty;

                    mainVm.OpenCodeBlockEditor(initialCode: string.Empty, languageHint: langHint, insertionIndex: 0);
                    return;
                }

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

    private void OnMessageInputTextInput(object? sender, TextInputEventArgs e)
    {
        try
        {
            if (DataContext is not MainChatViewModel mainVm || mainVm.ActiveConversation is null) return;
            if (_messageInputBox is null) return;

            string? inputText = e.Text;
            if (string.IsNullOrEmpty(inputText)) return;

            // Check if typing a single backtick that completes 3 consecutive backticks
            if (inputText == "`")
            {
                int caret = _messageInputBox.CaretIndex;
                string current = _messageInputBox.Text ?? string.Empty;

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
                    string updated = current.Remove(caret - 2, 2);
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
                int caret = _messageInputBox.CaretIndex;
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

        string selectedText = _messageInputBox?.SelectedText ?? string.Empty;
        int caretIndex = _messageInputBox?.CaretIndex ?? -1;

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

    private async void OnSearchInputKeyDown(object? sender, KeyEventArgs e)
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

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            _searchInputBox?.Focus();
            _searchInputBox?.SelectAll();
            e.Handled = true;
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
