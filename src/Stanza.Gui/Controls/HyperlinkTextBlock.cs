using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Stanza.Gui.Helpers;
using Stanza.Gui.Helpers.Markdown;
using Stanza.Storage.Models;

namespace Stanza.Gui.Controls;

public class HyperlinkTextBlock : TextBlock
{
    private static readonly IBrush DefaultInboundBrush = new SolidColorBrush(Color.Parse("#00F0FF"));  // Electric Cyan
    private static readonly IBrush DefaultOutboundBrush = new SolidColorBrush(Color.Parse("#BAE6FD")); // Tailwind Sky 200

    public static readonly StyledProperty<MessageDirection> MessageDirectionProperty =
        AvaloniaProperty.Register<HyperlinkTextBlock, MessageDirection>(
            nameof(MessageDirection),
            defaultValue: MessageDirection.Inbound);

    public static readonly StyledProperty<bool> IsMarkdownEnabledProperty =
        AvaloniaProperty.Register<HyperlinkTextBlock, bool>(
            nameof(IsMarkdownEnabled),
            defaultValue: true);

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

    public bool IsMarkdownEnabled
    {
        get => GetValue(IsMarkdownEnabledProperty);
        set => SetValue(IsMarkdownEnabledProperty, value);
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
                 change.Property == IsMarkdownEnabledProperty ||
                 change.Property == InboundLinkBrushProperty ||
                 change.Property == OutboundLinkBrushProperty ||
                 change.Property == FontSizeProperty ||
                 change.Property == FontFamilyProperty ||
                 change.Property == ForegroundProperty)
        {
            UpdateInlines();
        }
    }

    public void UpdateInlines()
    {
        var text = _rawText ?? Text;
        if (string.IsNullOrEmpty(text))
        {
            Inlines?.Clear();
            return;
        }

        var linkBrush = MessageDirection == MessageDirection.Outbound
            ? (OutboundLinkBrush ?? DefaultOutboundBrush)
            : (InboundLinkBrush ?? DefaultInboundBrush);

        if (IsMarkdownEnabled && MarkdownParser.HasMarkdownOrLinks(text))
        {
            _isUpdatingInlines = true;
            try
            {
                SetCurrentValue(TextProperty, null);
                Inlines ??= new InlineCollection();
                Inlines.Clear();

                var doc = MarkdownParser.Parse(text);
                var context = new MarkdownRenderContext(
                    FontSize: this.FontSize,
                    FontFamily: this.FontFamily,
                    Foreground: this.Foreground,
                    LinkBrush: linkBrush,
                    OpenUrlAction: OpenUrlAction,
                    CopyUrlAction: CopyUrlAction,
                    MessageDirection: MessageDirection);

                MarkdownRenderer.RenderToInlines(doc, Inlines, context);
                return;
            }
            finally
            {
                _isUpdatingInlines = false;
            }
        }

        // When Markdown is disabled or text does not contain markdown/links
        var segments = LinkParser.Parse(text);
        var hasLinks = false;
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

            var fallbackContext = new MarkdownRenderContext(
                FontSize: this.FontSize,
                FontFamily: this.FontFamily,
                Foreground: this.Foreground,
                LinkBrush: linkBrush,
                OpenUrlAction: OpenUrlAction,
                CopyUrlAction: CopyUrlAction,
                MessageDirection: MessageDirection);

            foreach (var segment in segments)
            {
                if (!segment.IsLink)
                {
                    Inlines.Add(new Run(segment.Text));
                }
                else
                {
                    var navUrl = segment.NavigateUri ?? segment.Text;
                    var container = MarkdownRenderer.CreateLinkContainer(new MarkdownLink(segment.Text, navUrl), fallbackContext);
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
