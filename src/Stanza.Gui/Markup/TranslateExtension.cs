using System;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Stanza.Gui.Markup;

public sealed class TranslateExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public TranslateExtension()
    {
    }

    public TranslateExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
            return string.Empty;

        if (serviceProvider == null)
            return Services.LocalizationManager.Instance.GetString(Key);

        return new DynamicResourceExtension(Key).ProvideValue(serviceProvider);
    }
}
