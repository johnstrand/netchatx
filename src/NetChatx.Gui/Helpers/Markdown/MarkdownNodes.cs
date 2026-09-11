using System.Collections.Generic;

namespace NetChatx.Gui.Helpers.Markdown;

public interface IMarkdownBlock { }

public interface IMarkdownInline { }

public sealed record MarkdownDocument(IReadOnlyList<IMarkdownBlock> Blocks);

public sealed record MarkdownParagraph(IReadOnlyList<IMarkdownInline> Inlines) : IMarkdownBlock;

public sealed record MarkdownHeader(int Level, IReadOnlyList<IMarkdownInline> Inlines) : IMarkdownBlock;

public sealed record MarkdownCodeBlock(string Code, string? Language = null) : IMarkdownBlock;

public sealed record MarkdownBlockquote(IReadOnlyList<IMarkdownBlock> Blocks) : IMarkdownBlock;

public sealed record MarkdownList(bool IsOrdered, IReadOnlyList<MarkdownListItem> Items) : IMarkdownBlock;

public sealed record MarkdownListItem(int Number, IReadOnlyList<IMarkdownInline> Inlines);

// Inlines
public sealed record MarkdownText(string Text) : IMarkdownInline;

public sealed record MarkdownBold(IReadOnlyList<IMarkdownInline> Inlines) : IMarkdownInline;

public sealed record MarkdownItalic(IReadOnlyList<IMarkdownInline> Inlines) : IMarkdownInline;

public sealed record MarkdownBoldItalic(IReadOnlyList<IMarkdownInline> Inlines) : IMarkdownInline;

public sealed record MarkdownStrikethrough(IReadOnlyList<IMarkdownInline> Inlines) : IMarkdownInline;

public sealed record MarkdownInlineCode(string Code) : IMarkdownInline;

public sealed record MarkdownLink(string Text, string Url) : IMarkdownInline;

public sealed record MarkdownLineBreak : IMarkdownInline;
