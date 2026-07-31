using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Testbench.Core.I18n;

namespace Testbench.Gui;

/// <summary>
/// Makes the text catalog bindable. One instance, an indexer, and a single
/// PropertyChanged for "Item[]" when the language changes, which is what tells
/// every binding in every window to fetch its text again.
///
/// Without this a language switch would need a window restart, and a tool that
/// has to be restarted to change its language will be left in the wrong one.
/// </summary>
public sealed class LocProxy : INotifyPropertyChanged
{
    public static LocProxy I { get; } = new();

    private LocProxy()
    {
        Loc.Changed += Refresh;
    }

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh()
    {
        // Binding.IndexerName is "Item[]": the signal for "every indexed value
        // changed". The dispatcher hop matters because a language can be switched
        // from any thread.
        void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));

        var app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess()) Raise();
        else app.Dispatcher.BeginInvoke(Raise);
    }
}

/// <summary>
/// XAML shorthand: <c>Text="{local:Tr gui.run}"</c>.
///
/// Falls back to the key itself, so a typo shows up as "gui.run" in the window
/// instead of an empty label nobody notices.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocProxy.I,
            Mode = BindingMode.OneWay,
            FallbackValue = Key,
            TargetNullValue = Key,
        };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>One entry of the language menu.</summary>
public sealed record LanguageChoice(string Code, string NativeName)
{
    public string Label => Code == NativeName ? Code : $"{NativeName}";

    public override string ToString() => Label;
}
