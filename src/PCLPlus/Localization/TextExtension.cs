using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

namespace PCLPlus.Localization
{
    /// <summary>
    /// XAML markup extension that binds to a localized string and updates when the language changes.
    /// </summary>
    /// <example><code>&lt;Button Content="{loc:Text Main.Browse}" /&gt;</code></example>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class TextExtension : MarkupExtension
    {
        public TextExtension()
        {
        }

        public TextExtension(string key)
        {
            Key = key;
        }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizedStrings.Instance,
                Mode = BindingMode.OneWay,
            };
            return binding.ProvideValue(serviceProvider);
        }
    }

    /// <summary>
    /// Binding source exposing <see cref="Text"/> through an indexer.
    /// </summary>
    public sealed class LocalizedStrings : INotifyPropertyChanged
    {
        public static LocalizedStrings Instance { get; } = new LocalizedStrings();

        private LocalizedStrings()
        {
            Text.LanguageChanged += (sender, e) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string this[string key] => Text.Get(key);
    }
}
