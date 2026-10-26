using System.IO;
using System.Windows;
using MainStay.ViewModels;
using Microsoft.Win32;

namespace MainStay
{
  // ReSharper disable once UnusedMember.Global
  public partial class MainWindow
  {
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
      InitializeComponent();
      DataContext = _viewModel;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
      var dialog = new OpenFolderDialog
      {
        Title = "Select the folder to search for git repositories",
        InitialDirectory = _viewModel.RootPath
      };
      if (dialog.ShowDialog(this) == true)
      {
        _viewModel.RootPath = dialog.FolderName;
      }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
      var dialog = new SaveFileDialog
      {
        Filter = "JSON (*.json)|*.json|CSV (*.csv)|*.csv",
        FileName = "mainstay-results"
      };
      if (dialog.ShowDialog(this) != true)
      {
        return;
      }

      try
      {
        await _viewModel.ExportAsync(dialog.FileName);
      }
      catch (IOException exception)
      {
        MessageBox.Show(this, exception.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }
  }
}
