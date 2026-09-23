using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanza.Gui.ViewModels;

namespace Stanza.Gui.Views;

public partial class CodeBlockEditorView : UserControl
{
    private TextBox? _codeEditorTextBox;
    private ScrollViewer? _lineNumbersScrollViewer;
    private ScrollViewer? _textBoxScrollViewer;

    public CodeBlockEditorView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _codeEditorTextBox = this.FindControl<TextBox>("CodeEditorTextBox");
        _lineNumbersScrollViewer = this.FindControl<ScrollViewer>("LineNumbersScrollViewer");

        if (_codeEditorTextBox is not null)
        {
            _codeEditorTextBox.AddHandler(InputElement.KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
            
            // Sync line numbers scroll with text box scroll
            Dispatcher.UIThread.Post(() =>
            {
                HookScrollViewerSync();
                _codeEditorTextBox?.Focus();
            }, DispatcherPriority.Loaded);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_codeEditorTextBox is not null)
        {
            _codeEditorTextBox.RemoveHandler(InputElement.KeyDownEvent, OnEditorKeyDown);
        }

        if (_textBoxScrollViewer is not null)
        {
            _textBoxScrollViewer.ScrollChanged -= OnTextBoxScrollChanged;
            _textBoxScrollViewer = null;
        }

        base.OnUnloaded(e);
    }

    private void HookScrollViewerSync()
    {
        if (_codeEditorTextBox is null || _textBoxScrollViewer is not null) return;

        // Try to find the PART_ScrollViewer within the TextBox template
        _textBoxScrollViewer = _codeEditorTextBox.FindDescendantOfType<ScrollViewer>();
        if (_textBoxScrollViewer is not null)
        {
            _textBoxScrollViewer.ScrollChanged += OnTextBoxScrollChanged;
        }
    }

    private void OnTextBoxScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_lineNumbersScrollViewer is not null && _textBoxScrollViewer is not null)
        {
            _lineNumbersScrollViewer.Offset = new Vector(0, _textBoxScrollViewer.Offset.Y);
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CodeBlockEditorViewModel vm) return;

        if (e.Key == Key.Escape)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Return &&
            (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            vm.InsertCommand.Execute(null);
            e.Handled = true;
            return;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is CodeBlockEditorViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CodeBlockEditorViewModel.IsOpen))
        {
            if (DataContext is CodeBlockEditorViewModel { IsOpen: true })
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _codeEditorTextBox?.Focus();
                    if (_codeEditorTextBox is not null)
                    {
                        _codeEditorTextBox.CaretIndex = _codeEditorTextBox.Text?.Length ?? 0;
                    }
                }, DispatcherPriority.Input);
            }
        }
    }
}
