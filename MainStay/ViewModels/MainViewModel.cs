using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using MainStay.Models;
using MainStay.Models.Repository;
using MainStay.Services;

namespace MainStay.ViewModels
{
  public sealed class MainViewModel : ObservableObject
  {
    private readonly Runner _runner = new();
    private CancellationTokenSource? _cancellationTokenSource;

    public MainViewModel()
    {
      RunCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning);
      CancelCommand = new AsyncRelayCommand(CancelAsync, () => IsRunning);
    }

    public ObservableCollection<RepositoryResult> Results { get; } = [];

    public ICommand RunCommand { get; }

    public ICommand CancelCommand { get; }

    public string RootPath
    {
      get;
      set => SetProperty(ref field, value);
    } = Environment.CurrentDirectory;

    public string BranchNames
    {
      get;
      set => SetProperty(ref field, value);
    } = "main, master";

    public int ThrottleLimit
    {
      get;
      set => SetProperty(ref field, Math.Clamp(value, 1, 64));
    } = 10;

    public int TimeoutSeconds
    {
      get;
      set => SetProperty(ref field, Math.Clamp(value, 5, 3600));
    } = 120;

    public bool StayOnMain
    {
      get;
      set => SetProperty(ref field, value);
    }

    public bool IsNotRunning => !IsRunning;

    public bool IsRunning
    {
      get;
      private set
      {
        if (SetProperty(ref field, value))
        {
          OnPropertyChanged(nameof(IsNotRunning));
          OnPropertyChanged(nameof(CanExport));
        }
      }
    }

    public bool CanExport => !IsRunning && Results.Count > 0;

    public int ProgressValue
    {
      get;
      private set => SetProperty(ref field, value);
    }

    public int ProgressMaximum
    {
      get;
      private set => SetProperty(ref field, value);
    } = 1;

    public string StatusText
    {
      get;
      private set => SetProperty(ref field, value);
    } = "Ready.";

    public Task ExportAsync(string path) => ResultExporter.ExportAsync(path, [.. Results], CancellationToken.None);

    private async Task RunAsync()
    {
      var branchNames = BranchNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (!Directory.Exists(RootPath))
      {
        StatusText = "The root folder does not exist.";
        return;
      }

      if (branchNames.Length == 0)
      {
        StatusText = "Enter at least one main branch name.";
        return;
      }

      var viewOptions = new ViewOptions
      {
        RootPath = RootPath,
        MainBranchNames = branchNames,
        ThrottleLimit = ThrottleLimit,
        TimeoutSeconds = TimeoutSeconds,
        StayOnMain = StayOnMain
      };

      IsRunning = true;
      Results.Clear();
      ProgressValue = 0;
      StatusText = "Searching for repositories...";
      _cancellationTokenSource = new CancellationTokenSource();

      try
      {
        var results = await _runner.RunAsync(viewOptions, new Progress<RunProgress>(OnProgress), _cancellationTokenSource.Token);

        Results.Clear();
        foreach (var result in results)
        {
          Results.Add(result);
        }

        var failed = results.Count(result => result.Status == RepositoryStatus.Error);
        StatusText = results.Count == 0 ? $"No git repositories found under '{RootPath}'." : $"Done: {results.Count} repositories, {failed} failed.";
      }
      catch (OperationCanceledException)
      {
        StatusText = "Cancelled.";
      }
      catch (Win32Exception exception)
      {
        StatusText = $"Could not start git: {exception.Message}";
      }
      catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
      {
        StatusText = $"Run failed: {exception.Message}";
      }
      finally
      {
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = null;
        IsRunning = false;
        OnPropertyChanged(nameof(CanExport));
      }
    }

    private Task CancelAsync()
    {
      _cancellationTokenSource?.Cancel();
      return Task.CompletedTask;
    }

    private void OnProgress(RunProgress progress)
    {
      ProgressMaximum = Math.Max(progress.Total, 1);
      ProgressValue = progress.Completed;
      StatusText = $"{progress.Completed} of {progress.Total} complete";

      if (progress.Latest is not null)
      {
        Results.Add(progress.Latest);
      }
    }
  }
}
