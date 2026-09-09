using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using NetChatx.Tui.Commands;
using NetChatx.Tui.Models;
using NetChatx.Tui.Themes;

namespace NetChatx.Tui.Views;

public sealed class MainChatWindow : Window
{
    private readonly FrameView _leftPane;
    private readonly FrameView _chatPane;
    private readonly ListView _bufferListView;
    private readonly ListView _rosterListView;
    private readonly ListView _messageListView;
    private readonly Label _chatHeaderLabel;
    private readonly Label _statusLabel;
    private readonly TextField _inputField;

    private readonly List<ChatBuffer> _buffers = [];
    private readonly ObservableCollection<string> _bufferCollection = [];
    private readonly ObservableCollection<string> _rosterCollection = [];
    private readonly ObservableCollection<string> _messageCollection = [];
    private int _activeBufferIndex = 0;

    public event Func<string, Task>? OnCommandSubmitted;
    public event Func<string, Task>? OnContactActivated;
    public event Func<ChatBuffer, Task>? OnBufferSwitched;

    public ChatBuffer ActiveBuffer => _buffers.Count > 0 ? _buffers[_activeBufferIndex] : _consoleBuffer;
    private readonly ChatBuffer _consoleBuffer = new("console", "Console", BufferType.Console);

    public MainChatWindow()
    {
        Title = "NetChatx - XMPP Client (.NET 10)";
        SetScheme(ThemeManager.GetColorScheme(TuiTheme.Catppuccin));

        _buffers.Add(_consoleBuffer);
        _consoleBuffer.AddSystemMessage("Welcome to NetChatx! Type /help for available commands.");

        // Left Pane (Buffers + Roster)
        _leftPane = new FrameView
        {
            Title = "Chats & Roster",
            X = 0,
            Y = 0,
            Width = 30,
            Height = Dim.Fill(2)
        };

        var buffersLabel = new Label
        {
            Text = "Buffers (Alt+1..9):",
            X = 0,
            Y = 0
        };

        _bufferListView = new ListView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Percent(50) - 1
        };
        _bufferListView.SetSource(_bufferCollection);
        _bufferListView.ValueChanged += (sender, args) =>
        {
            SwitchBuffer(_bufferListView.SelectedItem ?? 0);
        };

        var rosterLabel = new Label
        {
            Text = "Contacts (Enter to chat):",
            X = 0,
            Y = Pos.Bottom(_bufferListView)
        };

        _rosterListView = new ListView
        {
            X = 0,
            Y = Pos.Bottom(rosterLabel),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _rosterListView.SetSource(_rosterCollection);
        _rosterListView.Activated += (sender, args) =>
        {
            if (_rosterListView.SelectedItem.HasValue)
            {
                int sel = _rosterListView.SelectedItem.Value;
                if (sel >= 0 && sel < _rosterCollection.Count)
                {
                    _ = OnContactActivated?.Invoke(_rosterCollection[sel]);
                }
            }
        };

        _rosterListView.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent.KeyCode == KeyCode.Enter && _rosterListView.SelectedItem.HasValue)
            {
                int sel = _rosterListView.SelectedItem.Value;
                if (sel >= 0 && sel < _rosterCollection.Count)
                {
                    _ = OnContactActivated?.Invoke(_rosterCollection[sel]);
                    keyEvent.Handled = true;
                }
            }
        };

        _leftPane.Add(buffersLabel, _bufferListView, rosterLabel, _rosterListView);

        // Chat Pane
        _chatPane = new FrameView
        {
            Title = "Conversation",
            X = Pos.Right(_leftPane),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2)
        };

        _chatHeaderLabel = new Label
        {
            Text = " [Console Buffer] ",
            X = 0,
            Y = 0,
            Width = Dim.Fill()
        };

        _messageListView = new ListView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _messageListView.SetSource(_messageCollection);

        _chatPane.Add(_chatHeaderLabel, _messageListView);

        // Status Line
        _statusLabel = new Label
        {
            Text = " [Disconnected] | Type /connect <jid> <pass> to start ",
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill()
        };

        // Input Line
        _inputField = new TextField
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1
        };

        _inputField.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent.KeyCode == KeyCode.Enter)
            {
                string text = _inputField.Text?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _inputField.Text = "";
                    _ = OnCommandSubmitted?.Invoke(text);
                }
                keyEvent.Handled = true;
            }
            else if (keyEvent.KeyCode == KeyCode.Tab)
            {
                string cur = _inputField.Text?.ToString() ?? "";
                var comp = TuiCommandProcessor.GetCompletions(cur, _rosterCollection);
                if (comp.Length > 0)
                {
                    _inputField.Text = comp[0];
                    _inputField.InsertionPoint = _inputField.Text.Length;
                }
                keyEvent.Handled = true;
            }
            else if (keyEvent.IsAlt && keyEvent.NoAlt.KeyCode >= KeyCode.D1 && keyEvent.NoAlt.KeyCode <= KeyCode.D9)
            {
                int bufIdx = (int)(keyEvent.NoAlt.KeyCode - KeyCode.D1);
                SwitchBuffer(bufIdx);
                keyEvent.Handled = true;
            }
        };

        KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent.IsAlt && keyEvent.NoAlt.KeyCode >= KeyCode.D1 && keyEvent.NoAlt.KeyCode <= KeyCode.D9)
            {
                int bufIdx = (int)(keyEvent.NoAlt.KeyCode - KeyCode.D1);
                SwitchBuffer(bufIdx);
                keyEvent.Handled = true;
            }
        };

        Add(_leftPane, _chatPane, _statusLabel, _inputField);
        UpdateBufferList();
        RefreshActiveBufferView();
    }

    public void SetStatus(string statusText)
    {
        Application.Invoke(() =>
        {
            _statusLabel.Text = $" {statusText} ";
        });
    }

    public void UpdateRoster(IEnumerable<string> contacts)
    {
        Application.Invoke(() =>
        {
            _rosterCollection.Clear();
            foreach (var c in contacts)
            {
                _rosterCollection.Add(c);
            }
        });
    }

    public ChatBuffer GetOrCreateBuffer(string id, string title, BufferType type, Core.Jid? remoteJid = null)
    {
        var existing = _buffers.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var buffer = new ChatBuffer(id, title, type, remoteJid);
        _buffers.Add(buffer);
        UpdateBufferList();
        return buffer;
    }

    public void SwitchBuffer(int index)
    {
        if (index >= 0 && index < _buffers.Count)
        {
            bool changed = _activeBufferIndex != index;
            _activeBufferIndex = index;
            if (_bufferListView.SelectedItem != index)
            {
                _bufferListView.SelectedItem = index;
            }
            RefreshActiveBufferView();
            if (changed)
            {
                _ = OnBufferSwitched?.Invoke(_buffers[_activeBufferIndex]);
            }
        }
    }

    public void SwitchToBuffer(ChatBuffer buffer)
    {
        int idx = _buffers.IndexOf(buffer);
        if (idx >= 0)
        {
            SwitchBuffer(idx);
        }
    }

    public void SwitchToBuffer(string id)
    {
        int idx = _buffers.FindIndex(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            SwitchBuffer(idx);
        }
    }

    public void NextBuffer()
    {
        if (_buffers.Count > 0)
        {
            _activeBufferIndex = (_activeBufferIndex + 1) % _buffers.Count;
            _bufferListView.SelectedItem = _activeBufferIndex;
            RefreshActiveBufferView();
        }
    }

    public void PrevBuffer()
    {
        if (_buffers.Count > 0)
        {
            _activeBufferIndex = (_activeBufferIndex - 1 + _buffers.Count) % _buffers.Count;
            _bufferListView.SelectedItem = _activeBufferIndex;
            RefreshActiveBufferView();
        }
    }

    public void CloseActiveBuffer()
    {
        if (ActiveBuffer.Type != BufferType.Console)
        {
            _buffers.RemoveAt(_activeBufferIndex);
            if (_activeBufferIndex >= _buffers.Count)
                _activeBufferIndex = _buffers.Count - 1;
            UpdateBufferList();
            RefreshActiveBufferView();
        }
    }

    public void RefreshActiveBufferView()
    {
        Application.Invoke(() =>
        {
            var buffer = ActiveBuffer;
            string lockIcon = buffer.IsEncrypted ? "🔒 OMEMO" : "Plain";
            _chatHeaderLabel.Text = $" [{lockIcon}] {buffer.Title} ";

            _messageCollection.Clear();
            foreach (var line in buffer.DisplayLines)
            {
                _messageCollection.Add(line);
            }

            if (_messageCollection.Count > 0)
            {
                _messageListView.SelectedItem = _messageCollection.Count - 1;
            }
        });
    }

    private void UpdateBufferList()
    {
        Application.Invoke(() =>
        {
            _bufferCollection.Clear();
            for (int i = 0; i < _buffers.Count; i++)
            {
                var b = _buffers[i];
                string unread = b.UnreadCount > 0 ? $" ({b.UnreadCount})" : "";
                _bufferCollection.Add($"{i + 1}. {b.Title}{unread}");
            }
            _bufferListView.SelectedItem = _activeBufferIndex;
        });
    }
}
