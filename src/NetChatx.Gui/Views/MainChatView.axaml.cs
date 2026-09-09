using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NetChatx.Gui.ViewModels;

namespace NetChatx.Gui.Views;

public partial class MainChatView : UserControl
{
    private ChatConversationViewModel? _currentConversation;
    private ScrollViewer? _messagesScrollViewer;
    private TextBox? _messageInputBox;

    public MainChatView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _messagesScrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer");
        _messageInputBox = this.FindControl<TextBox>("MessageInputBox");

        if (_messageInputBox is not null)
        {
            _messageInputBox.AddHandler(InputElement.KeyDownEvent, OnMessageInputKeyDown, RoutingStrategies.Tunnel);
        }

        if (_messagesScrollViewer is not null)
        {
            _messagesScrollViewer.SizeChanged += OnMessagesScrollViewerSizeChanged;
        }

        if (DataContext is MainChatViewModel vm)
        {
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            UpdateActiveConversation(vm.ActiveConversation);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_messageInputBox is not null)
        {
            _messageInputBox.RemoveHandler(InputElement.KeyDownEvent, OnMessageInputKeyDown);
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
        }
        else
        {
            UpdateActiveConversation(null);
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainChatViewModel.ActiveConversation))
        {
            if (sender is MainChatViewModel vm)
            {
                UpdateActiveConversation(vm.ActiveConversation);
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
        }

        _currentConversation = newConversation;

        if (_currentConversation is not null)
        {
            _currentConversation.Messages.CollectionChanged += OnMessagesCollectionChanged;
            _currentConversation.ScrollToBottomRequested += ScrollToLatestMessage;
            ScrollToLatestMessage();
            _messageInputBox?.Focus();
        }
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

    private void OnMessageInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Shift+Enter allows newline insertion
                return;
            }

            // Enter without Shift: Send message
            e.Handled = true;

            if (sender is TextBox textBox &&
                DataContext is MainChatViewModel mainVm &&
                mainVm.ActiveConversation is { } conv)
            {
                if (!string.IsNullOrEmpty(textBox.Text) && conv.InputText != textBox.Text)
                {
                    conv.InputText = textBox.Text;
                }

                if (conv.SendMessageCommand.CanExecute(null))
                {
                    conv.SendMessageCommand.Execute(null);
                    textBox.Text = string.Empty;
                }
            }
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
