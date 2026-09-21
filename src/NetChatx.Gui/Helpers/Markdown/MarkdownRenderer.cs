using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NetChatx.Storage.Models;

namespace NetChatx.Gui.Helpers.Markdown;

public sealed record MarkdownRenderContext(
    double FontSize,
    FontFamily? FontFamily = null,
    IBrush? Foreground = null,
    IBrush? LinkBrush = null,
    FontWeight FontWeight = FontWeight.Normal,
    FontStyle FontStyle = FontStyle.Normal,
    TextDecorationCollection? TextDecorations = null,
    Action<string>? OpenUrlAction = null,
    Func<string, Task>? CopyUrlAction = null,
    MessageDirection MessageDirection = MessageDirection.Inbound);

public static class MarkdownRenderer
{
    public static void RenderToInlines(MarkdownDocument document, InlineCollection inlines, MarkdownRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(inlines);
        ArgumentNullException.ThrowIfNull(context);

        inlines.Clear();

        for (int b = 0; b < document.Blocks.Count; b++)
        {
            var block = document.Blocks[b];
            var isLast = b == document.Blocks.Count - 1;

            if (block is MarkdownParagraph paragraph)
            {
                RenderInlines(paragraph.Inlines, inlines, context);
                if (!isLast)
                {
                    inlines.Add(new LineBreak());
                }
            }
            else if (block is MarkdownHeader header)
            {
                if (inlines.Count > 0) inlines.Add(new LineBreak());

                var sizeMultiplier = header.Level switch
                {
                    1 => 1.35,
                    2 => 1.20,
                    3 => 1.10,
                    _ => 1.05
                };

                var headerContext = context with
                {
                    FontSize = context.FontSize * sizeMultiplier,
                    FontWeight = FontWeight.Bold
                };

                RenderInlines(header.Inlines, inlines, headerContext);
                if (!isLast) inlines.Add(new LineBreak());
            }
            else if (block is MarkdownCodeBlock codeBlock)
            {
                if (inlines.Count > 0) inlines.Add(new LineBreak());

                var container = CreateCodeBlockContainer(codeBlock, context);
                inlines.Add(container);
                if (!isLast) inlines.Add(new LineBreak());
            }
            else if (block is MarkdownBlockquote quote)
            {
                if (inlines.Count > 0) inlines.Add(new LineBreak());

                var container = CreateBlockquoteContainer(quote, context);
                inlines.Add(container);
                if (!isLast) inlines.Add(new LineBreak());
            }
            else if (block is MarkdownList list)
            {
                if (inlines.Count > 0) inlines.Add(new LineBreak());

                for (int i = 0; i < list.Items.Count; i++)
                {
                    var item = list.Items[i];
                    var prefix = list.IsOrdered ? $"{item.Number}. " : "•  ";
                    var run = new Run(prefix)
                    {
                        FontWeight = FontWeight.SemiBold,
                        FontSize = context.FontSize
                    };
                    if (context.Foreground is not null) run.Foreground = context.Foreground;
                    if (context.FontFamily is not null) run.FontFamily = context.FontFamily;
                    inlines.Add(run);

                    RenderInlines(item.Inlines, inlines, context);
                    if (i < list.Items.Count - 1 || !isLast)
                    {
                        inlines.Add(new LineBreak());
                    }
                }
            }
        }
    }

    public static void RenderInlines(IReadOnlyList<IMarkdownInline> markdownInlines, InlineCollection target, MarkdownRenderContext context)
    {
        foreach (var inline in markdownInlines)
        {
            if (inline is MarkdownText text)
            {
                var run = new Run(text.Text);
                if (context.Foreground is not null) run.Foreground = context.Foreground;
                if (context.FontFamily is not null) run.FontFamily = context.FontFamily;
                run.FontSize = context.FontSize;
                if (context.FontWeight != FontWeight.Normal) run.FontWeight = context.FontWeight;
                if (context.FontStyle != FontStyle.Normal) run.FontStyle = context.FontStyle;
                if (context.TextDecorations is not null) run.TextDecorations = context.TextDecorations;
                target.Add(run);
            }
            else if (inline is MarkdownBold bold)
            {
                RenderInlines(bold.Inlines, target, context with { FontWeight = FontWeight.Bold });
            }
            else if (inline is MarkdownItalic italic)
            {
                RenderInlines(italic.Inlines, target, context with { FontStyle = FontStyle.Italic });
            }
            else if (inline is MarkdownBoldItalic boldItalic)
            {
                RenderInlines(boldItalic.Inlines, target, context with { FontWeight = FontWeight.Bold, FontStyle = FontStyle.Italic });
            }
            else if (inline is MarkdownStrikethrough strike)
            {
                RenderInlines(strike.Inlines, target, context with { TextDecorations = Avalonia.Media.TextDecorations.Strikethrough });
            }
            else if (inline is MarkdownInlineCode code)
            {
                var codeBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(40, 0, 240, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 240, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1),
                    Margin = new Thickness(1, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = code.Code,
                        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                        FontSize = Math.Max(9.5, context.FontSize * 0.92),
                        Foreground = context.MessageDirection == MessageDirection.Outbound ? Brushes.White : new SolidColorBrush(Color.Parse("#38BDF8")),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                target.Add(new InlineUIContainer(codeBorder)
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
            }
            else if (inline is MarkdownLink link)
            {
                var linkContainer = CreateLinkContainer(link, context);
                target.Add(linkContainer);
            }
            else if (inline is MarkdownLineBreak)
            {
                target.Add(new LineBreak());
            }
        }
    }

    private static readonly object MenuInitLock = new();

    static MarkdownRenderer()
    {
        lock (MenuInitLock)
        {
            try
            {
                _ = new MenuItem();
            }
            catch { }
        }
    }

    internal static InlineUIContainer CreateLinkContainer(MarkdownLink link, MarkdownRenderContext context)
    {
        var linkTb = new TextBlock
        {
            Text = link.Text,
            TextDecorations = Avalonia.Media.TextDecorations.Underline,
            Foreground = context.LinkBrush,
            FontSize = context.FontSize,
            FontWeight = context.FontWeight,
            FontStyle = context.FontStyle,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        };
        if (context.FontFamily is not null) linkTb.FontFamily = context.FontFamily;

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

        ToolTip.SetTip(btn, link.Url);

        btn.Click += (s, e) =>
        {
            if (context.OpenUrlAction is not null) context.OpenUrlAction(link.Url);
            else UrlLauncher.OpenUrl(link.Url);
            e.Handled = true;
        };

        MenuFlyout flyout;
        MenuItem openItem;
        MenuItem copyItem;

        lock (MenuInitLock)
        {
            flyout = new MenuFlyout();
            openItem = new MenuItem { Header = "🌐 Open Link" };
            copyItem = new MenuItem { Header = "📋 Copy Link Address" };
            flyout.Items.Add(openItem);
            flyout.Items.Add(copyItem);
        }

        openItem.Click += (_, _) =>
        {
            if (context.OpenUrlAction is not null) context.OpenUrlAction(link.Url);
            else UrlLauncher.OpenUrl(link.Url);
        };

        copyItem.Click += async (_, _) =>
        {
            if (context.CopyUrlAction is not null) await context.CopyUrlAction(link.Url);
            else await UrlLauncher.CopyToClipboardAsync(link.Url);
        };

        btn.ContextFlyout = flyout;

        return new InlineUIContainer(btn)
        {
            BaselineAlignment = BaselineAlignment.Baseline
        };
    }

    private static InlineUIContainer CreateCodeBlockContainer(MarkdownCodeBlock codeBlock, MarkdownRenderContext context)
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        // Header with optional language and copy button
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var langDisplay = !string.IsNullOrEmpty(codeBlock.Language) ? codeBlock.Language : "code";
        var langTb = new TextBlock
        {
            Text = langDisplay,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#00F0FF")),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(langTb, 0);
        headerGrid.Children.Add(langTb);

        var copyBtn = new Button
        {
            Content = "📋 Copy",
            Classes = { "image-btn" },
            FontSize = 10,
            Padding = new Thickness(5, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#94A3B8"))
        };

        try
        {
            copyBtn.Cursor = Cursor.Parse("Hand");
        }
        catch { }
        copyBtn.Click += async (_, _) =>
        {
            await UrlLauncher.CopyToClipboardAsync(codeBlock.Code);
        };
        Grid.SetColumn(copyBtn, 1);
        headerGrid.Children.Add(copyBtn);

        Grid.SetRow(headerGrid, 0);
        grid.Children.Add(headerGrid);

        // Code content
        var codeTb = new TextBlock
        {
            Text = codeBlock.Code,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = Math.Max(10, context.FontSize * 0.9),
            Foreground = context.Foreground,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };
        Grid.SetRow(codeTb, 1);
        grid.Children.Add(codeTb);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#090D16")),
            BorderBrush = new SolidColorBrush(Color.Parse("#1E293B")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 3),
            MaxWidth = 540,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = grid
        };

        return new InlineUIContainer(border)
        {
            BaselineAlignment = BaselineAlignment.Center
        };
    }

    private static InlineUIContainer CreateBlockquoteContainer(MarkdownBlockquote quote, MarkdownRenderContext context)
    {
        var quoteInlines = new InlineCollection();
        for (int i = 0; i < quote.Blocks.Count; i++)
        {
            var b = quote.Blocks[i];
            if (b is MarkdownParagraph p)
            {
                RenderInlines(p.Inlines, quoteInlines, context with { FontStyle = FontStyle.Italic });
                if (i < quote.Blocks.Count - 1) quoteInlines.Add(new LineBreak());
            }
        }

        var quoteTb = new TextBlock
        {
            FontSize = context.FontSize,
            Foreground = context.Foreground,
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        };
        if (context.FontFamily is not null) quoteTb.FontFamily = context.FontFamily;

        quoteTb.Inlines ??= new InlineCollection();
        foreach (var inline in quoteInlines)
        {
            quoteTb.Inlines.Add(inline);
        }

        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = context.LinkBrush ?? new SolidColorBrush(Color.Parse("#00F0FF")),
            Padding = new Thickness(8, 2, 4, 2),
            Margin = new Thickness(0, 3),
            Child = quoteTb
        };

        return new InlineUIContainer(border)
        {
            BaselineAlignment = BaselineAlignment.Center
        };
    }
}
