using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Testbench.Gui;

/// <summary>
/// Code behind the one window. Everything with substance lives in
/// <see cref="MainViewModel"/>; this file wires clicks to it and keeps the log
/// scrolled to the bottom.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Follow the log while a run produces it. Switchable, because reading a
        // line you just spotted is impossible while it keeps jumping.
        ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            if (AutoScroll.IsChecked != true) return;
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
        };

        if (_vm.ConfigProblem is not null) _vm.RunDoctor();
    }

    private async void Run_Click(object sender, RoutedEventArgs e) => await _vm.RunAsync();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _vm.Cancel();

    private void Doctor_Click(object sender, RoutedEventArgs e) => _vm.RunDoctor();

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _vm.LogLines.Clear();

    private void Versions_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new VersionsWindow(_vm.Machine, _vm.MachinePath) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Changed) _vm.ReloadVersions();
    }

    private void VisualOk_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is PendingItem item) _vm.Answer(item, true);
    }

    private void VisualFail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is PendingItem item) _vm.Answer(item, false);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.TestedVersions))
        {
            MessageBox.Show(this,
                "Keine Version hat beide Stufen bestanden. Es gibt nichts zu melden.",
                "Kompatibilitaet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText($"TESTED_VERSIONS: \"{_vm.TestedVersions}\"");
    }

    private void Report_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm.WriteReport();
        if (path is not null) OpenInShell(path);
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string path) OpenInShell(path);
    }

    private void OpenBench_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(_vm.MachinePath);
        if (dir is not null) OpenInShell(dir);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_vm.Busy)
        {
            // Closing mid-run would leave the game running, the lock held and the
            // GamePrefs unrestored. That last one is the expensive part.
            var answer = MessageBox.Show(this,
                "Es laeuft noch ein Test. Beim Schliessen wird er abgebrochen, das Spiel beendet " +
                "und die GamePrefs zurueckgespielt. Trotzdem schliessen?",
                "Lauf aktiv", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) { e.Cancel = true; return; }
            _vm.Cancel();
        }
        _vm.SaveUiState();
    }

    private void OpenInShell(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            MessageBox.Show(this, $"Nicht gefunden:\n{path}", "Oeffnen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Oeffnen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
