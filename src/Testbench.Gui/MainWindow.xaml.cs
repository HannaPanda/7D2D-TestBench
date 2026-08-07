using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Testbench.Core.I18n;

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
            QueueScrollToEnd();
        };

        if (_vm.ConfigProblem is not null) _vm.RunDoctor();
    }

    private bool _scrollQueued;

    /// <summary>
    /// Scrolls the log to the end, but never from inside the CollectionChanged
    /// notification that asked for it.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>ScrollIntoView</c> forces a synchronous layout pass, and running that
    /// from inside <c>OnCollectionChanged</c> executes
    /// <c>ItemContainerGenerator.Verify()</c> at the one moment when the collection
    /// already holds the new item and the generator has not been told about it yet.
    /// It then reports "ItemsControl is inconsistent with its items source"
    /// (accumulated count N vs actual N+1) as an <see cref="InvalidOperationException"/>
    /// on the dispatcher, which nothing catches - the window is simply gone, mid-run,
    /// with the game still up and no run record written. Queued at
    /// <see cref="DispatcherPriority.Background"/> the notification has finished and
    /// the generator has caught up.
    ///
    /// Coalesced, because a tailed game log adds lines in bursts and one queued
    /// operation per line would be hundreds of layout passes for one visible result.
    /// </remarks>
    private void QueueScrollToEnd()
    {
        if (_scrollQueued) return;
        _scrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scrollQueued = false;
            if (AutoScroll.IsChecked != true) return;
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
        });
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
            MessageBox.Show(this, Loc.T("gui.copy.nothingToReport"), Loc.T("gui.compatibility"),
                MessageBoxButton.OK, MessageBoxImage.Information);
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
            var answer = MessageBox.Show(this, Loc.T("gui.closeWhileRunning"), Loc.T("gui.runActive"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) { e.Cancel = true; return; }
            _vm.Cancel();
        }
        _vm.SaveUiState();
    }

    private void OpenInShell(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            MessageBox.Show(this, Loc.T("gui.notFound", path), Loc.T("gui.open"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("gui.open"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
