using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NetChatx.Gui.Helpers;
using NetChatx.Storage.Models;

namespace NetChatx.Gui.Controls;

public class HyperlinkTextBlock : TextBlock
{
    private static readonly IBrush DefaultInboundBrush = new SolidColorBrush(Color.Parse("#60A5FA"));  // Tailwind Blue 400
    private static readonly IBrush DefaultOutboundBrush = new SolidColorBrush(Color.Parse("#BAE6FD")); // Tailwind Sky 200

    public static readonly StyledProperty<MessageDirection> MessageDirectionProperty =
        AvaloniaProperty.Register<HyperlinkTextBlock, MessageDirection>(
            nameof(MessageDirection),
            defaultValue: MessageDirection.Inbound);

    public static readonly StyledProperty<IBrush?> InboundLinkBrushProperty =
        AvaloniaProperty.Register<HyperlinkTextBlock, IBrush?>(nameof(InboundLinkBrush));

    public static readonly StyledProperty<IBrush?> OutboundLinkBrushProperty =
        AvaloniaProperty.Register<HyperlinkTextBlock, IBrush?>(nameof(OutboundLinkBrush));

    public static readonly StyledProperty<Action<string>?> OpenUrlActionProperty =
        AvaloniaProperty.Register<HyperlinkTextBlock, Action<string>?>(nameof(OpenUrlAction));

    public static readonly StyledProperty<Func<string, Task>?> CopyUrlActionProperty =
        AvaloniaProperty.Register<HyperlinkTextBlock, Func<string, Task>?>(nameof(CopyUrlAction));

    public MessageDirection MessageDirection
    {
        get => GetValue(MessageDirectionProperty);
        set => SetValue(MessageDirectionProperty, value);
    }

    public IBrush? InboundLinkBrush
    {
        get => GetValue(InboundLinkBrushProperty);
        set => SetValue(InboundLinkBrushProperty, value);
    }

    public IBrush? OutboundLinkBrush
    {
        get => GetValue(OutboundLinkBrushProperty);
        set => SetValue(OutboundLinkBrushProperty, value);
    }

    public Action<string>? OpenUrlAction
    {
        get => GetValue(OpenUrlActionProperty);
        set => SetValue(OpenUrlActionProperty, value);
    }

    public Func<string, Task>? CopyUrlAction
    {
        get => GetValue(CopyUrlActionProperty);
        set => SetValue(CopyUrlActionProperty, value);
    }

    public HyperlinkTextBlock()
    {
    }

    private string? _rawText;
    private bool _isUpdatingInlines;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            if (_isUpdatingInlines) return;
            _rawText = change.GetNewValue<string?>();
            UpdateInlines();
        }
        else if (change.Property == MessageDirectionProperty ||
                 change.Property == InboundLinkBrushProperty ||
                 change.Property == OutboundLinkBrushProperty ||
                 change.Property == FontSizeProperty ||
                 change.Property == FontFamilyProperty)
        {
            UpdateInlines();
        }
    }

    public void UpdateInlines()
    {
        string? text = _rawText ?? Text;
        if (string.IsNullOrEmpty(text))
        {
            Inlines?.Clear();
            return;
        }

        var segments = LinkParser.Parse(text);
        bool hasLinks = false;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].IsLink)
            {
                hasLinks = true;
                break;
            }
        }

        if (!hasLinks)
        {
            Inlines?.Clear();
            return;
        }

        _isUpdatingInlines = true;
        try
        {
            SetCurrentValue(TextProperty, null);
            Inlines ??= new InlineCollection();
            Inlines.Clear();

            var linkBrush = MessageDirection == MessageDirection.Outbound
                ? (OutboundLinkBrush ?? DefaultOutboundBrush)
                : (InboundLinkBrush ?? DefaultInboundBrush);

            foreach (var segment in segments)
            {
                if (!segment.IsLink)
                {
                    Inlines.Add(new Run(segment.Text));
                }
                else
                {
                    var navUrl = segment.NavigateUri ?? segment.Text;
                    var linkTb = new TextBlock
                    {
                        Text = segment.Text,
                        TextDecorations = Avalonia.Media.TextDecorations.Underline,
                        Foreground = linkBrush,
                        FontSize = this.FontSize,
                        FontFamily = this.FontFamily,
                        FontWeight = this.FontWeight,
                        FontStyle = this.FontStyle,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 520
                    };

                    var btn = new Button
                    {
                        Content = linkTb,
                        Classes = { "inline-link" },
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(0),
                        Margin = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    try
                    {
                        btn.Cursor = Cursor.Parse("Hand");
                    }
                    catch { }

                    ToolTip.SetTip(btn, navUrl);

                    btn.Click += (s, e) =>
                    {
                        if (OpenUrlAction is not null) OpenUrlAction(navUrl);
                        else UrlLauncher.OpenUrl(navUrl);
                        e.Handled = true;
                    };

                    var flyout = new MenuFlyout();
                    var openItem = new MenuItem { Header = "🌐 Open Link" };
                    openItem.Click += (_, _) =>
                    {
                        if (OpenUrlAction is not null) OpenUrlAction(navUrl);
                        else UrlLauncher.OpenUrl(navUrl);
                    };

                    var copyItem = new MenuItem { Header = "📋 Copy Link Address" };
                    copyItem.Click += async (_, _) =>
                    {
                        if (CopyUrlAction is not null) await CopyUrlAction(navUrl);
                        else await UrlLauncher.CopyToClipboardAsync(navUrl);
                    };

                    flyout.Items.Add(openItem);
                    flyout.Items.Add(copyItem);
                    btn.ContextFlyout = flyout;

                    var container = new InlineUIContainer(btn)
                    {
                        BaselineAlignment = BaselineAlignment.Baseline
                    };

                    Inlines.Add(container);
                }
            }
        }
        finally
        {
            _isUpdatingInlines = false;
        }
    }
}
