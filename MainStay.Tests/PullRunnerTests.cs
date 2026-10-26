using FluentAssertions;
using FluentAssertions.Execution;
using MainStay.Models;
using MainStay.Models.Repository;
using MainStay.Services;
using Xunit;

namespace MainStay.Tests
{
  public sealed class PullRunnerTests : IDisposable
  {
    private const int ThrottleLimit = 2;
    private const int TimeoutSeconds = 60;

    private readonly GitSandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public async Task RunAsync_WhenRepositoriesAreBehind_UpdatesAllAndReportsProgress()
    {
      // Arrange
      const int repositoryCount = 3;
      for (var index = 0; index < repositoryCount; index++)
      {
        _sandbox.CloneRepository($"repo-{index}");
      }

      _sandbox.PushNewCommit("upstream change");
      var viewOptions = new ViewOptions
      {
        RootPath = Path.Combine(_sandbox.Root, "repos"),
        MainBranchNames = ["main", "master"],
        ThrottleLimit = ThrottleLimit,
        TimeoutSeconds = TimeoutSeconds,
        StayOnMain = false
      };
      var progress = new CollectingProgress();

      // Act
      var results = await new Runner().RunAsync(viewOptions, progress, CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      results.Should().HaveCount(repositoryCount);
      results.Should().OnlyContain(result => result.Status == RepositoryStatus.Updated && result.Commits == 1);
      progress.Reports.Max(report => report.Completed).Should().Be(repositoryCount);
      progress.Reports.Max(report => report.Total).Should().Be(repositoryCount);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
      // Arrange
      _sandbox.CloneRepository("repo");
      var viewOptions = new ViewOptions
      {
        RootPath = Path.Combine(_sandbox.Root, "repos"),
        MainBranchNames = ["main", "master"],
        ThrottleLimit = ThrottleLimit,
        TimeoutSeconds = TimeoutSeconds,
        StayOnMain = false
      };
      using var cancellation = new CancellationTokenSource();
      await cancellation.CancelAsync();

      // Act
      var act = async () => await new Runner().RunAsync(viewOptions, new CollectingProgress(), cancellation.Token);

      // Assert
      await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class CollectingProgress : IProgress<RunProgress>
    {
      private readonly Lock _gate = new();
      private readonly List<RunProgress> _reports = [];

      public IReadOnlyList<RunProgress> Reports
      {
        get
        {
          lock (_gate)
          {
            return [.. _reports];
          }
        }
      }

      public void Report(RunProgress value)
      {
        lock (_gate)
        {
          _reports.Add(value);
        }
      }
    }
  }
}
