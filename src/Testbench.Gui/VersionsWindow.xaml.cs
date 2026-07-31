using System.Windows;
using Microsoft.Win32;
using Testbench.Core.Config;

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
            Title = "Installation oder uebergeordneten Ordner waehlen",
            InitialDirectory = System.IO.Directory.Exists(_vm.Root) ? _vm.Root : "",
        };
        if (dialog.ShowDialog(this) == true) _vm.AddFolder(dialog.FolderName);
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => _vm.Apply();

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not RegisteredRow row) return;

        var answer = MessageBox.Show(this,
            $"Eintrag '{row.Id}' entfernen?\n\nDie Installation unter\n{row.Dir}\nbleibt unangetastet.",
            "Version entfernen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) _vm.Remove(row);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
