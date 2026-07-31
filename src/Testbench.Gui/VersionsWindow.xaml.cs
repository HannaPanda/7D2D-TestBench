using System.Windows;
using Microsoft.Win32;
using Testbench.Core.Config;
using Testbench.Core.I18n;

namespace Testbench.Gui;

/// <summary>
/// Dialog for registering game versions. Opened from the main window, works on
/// the same <see cref="MachineConfig"/> instance, and writes it out as soon as
/// something was registered or removed.
/// </summary>
public partial class VersionsWindow : Window
{
    private readonly VersionsViewModel _vm;

    public VersionsWindow(MachineConfig machine, string machinePath)
    {
        InitializeComponent();
        _vm = new VersionsViewModel(machine, machinePath);
        DataContext = _vm;
        _vm.Scan();
    }

    /// <summary>Whether the main window has to reload its version list.</summary>
    public bool Changed => _vm.Changed;

    private void Scan_Click(object sender, RoutedEventArgs e) => _vm.Scan();

    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("gui.versions.pickFolder"),
            InitialDirectory = System.IO.Directory.Exists(_vm.Root) ? _vm.Root : "",
        };
        if (dialog.ShowDialog(this) == true) _vm.AddFolder(dialog.FolderName);
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => _vm.Apply();

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not RegisteredRow row) return;

        var answer = MessageBox.Show(this, Loc.T("gui.versions.confirmRemove", row.Id, row.Dir),
            Loc.T("gui.versions.removeTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) _vm.Remove(row);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
