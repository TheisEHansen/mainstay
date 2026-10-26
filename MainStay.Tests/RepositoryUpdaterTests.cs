using FluentAssertions;
using FluentAssertions.Execution;
using MainStay.Models;
using MainStay.Models.Repository;
using MainStay.Services.Git;
using MainStay.Services.Repository;
using Xunit;

namespace MainStay.Tests
{
  public sealed class RepositoryUpdaterTests : IDisposable
  {
    private const string FeatureBranch = "feature";
    private const string DefaultBranch = "develop";
    private const string UntrackedFile = "work.txt";
    private const int ThrottleLimit = 1;
    private const int TimeoutSeconds = 60;

    private readonly GitSandbox _sandbox = new();
    private readonly RepositoryUpdater _updater = new(new GitClient(TimeSpan.FromSeconds(TimeoutSeconds)));

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public async Task UpdateAsync_WhenBehindOrigin_PullsNewCommits()
    {
      // Arrange
      const int expectedCommits = 2;
      var repo = _sandbox.CloneRepository("behind");
      _sandbox.PushNewCommit("first");
      _sandbox.PushNewCommit("second");

      // Act
      var result = await _updater.UpdateAsync(repo, Options(), CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      result.Status.Should().Be(RepositoryStatus.Updated);
      result.Branch.Should().Be(DefaultBranch);
      result.Commits.Should().Be(expectedCommits);
    }

    [Fact]
    public async Task UpdateAsync_WhenOnFeatureBranchWithLocalChanges_ReturnsToBranchAndRestoresChanges()
    {
      // Arrange
      var repo = _sandbox.CloneRepository("feature-dirty");
      _sandbox.Git(repo, "checkout", "-b", FeatureBranch);
      await File.WriteAllTextAsync(Path.Combine(repo, UntrackedFile), "wip", TestContext.Current.CancellationToken);
      _sandbox.PushNewCommit("upstream change");

      // Act
      var result = await _updater.UpdateAsync(repo, Options(), CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      result.Status.Should().Be(RepositoryStatus.Updated);
      result.Stashed.Should().BeTrue();
      _sandbox.Git(repo, "branch", "--show-current").Should().Be(FeatureBranch);
      File.Exists(Path.Combine(repo, UntrackedFile)).Should().BeTrue();
      _sandbox.Git(repo, "stash", "list").Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_WhenStayOnMainWithLocalChanges_StaysOnMainAndKeepsStash()
    {
      // Arrange
      var repo = _sandbox.CloneRepository("stay");
      _sandbox.Git(repo, "checkout", "-b", FeatureBranch);
      await File.WriteAllTextAsync(Path.Combine(repo, UntrackedFile), "wip", TestContext.Current.CancellationToken);

      // Act
      var result = await _updater.UpdateAsync(repo, Options(true), CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      result.Status.Should().Be(RepositoryStatus.Updated);
      _sandbox.Git(repo, "branch", "--show-current").Should().Be(DefaultBranch);
      _sandbox.Git(repo, "stash", "list").Should().NotBeEmpty();
      File.Exists(Path.Combine(repo, UntrackedFile)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_WhenHeadIsDetached_ReturnsToOriginalCommit()
    {
      // Arrange
      var repo = _sandbox.CloneRepository("detached");
      var originalCommit = _sandbox.Git(repo, "rev-parse", "HEAD");
      _sandbox.Git(repo, "checkout", "--detach");
      _sandbox.PushNewCommit("upstream change");

      // Act
      var result = await _updater.UpdateAsync(repo, Options(), CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      result.Status.Should().Be(RepositoryStatus.Updated);
      _sandbox.Git(repo, "branch", "--show-current").Should().BeEmpty();
      _sandbox.Git(repo, "rev-parse", "HEAD").Should().Be(originalCommit);
    }

    [Fact]
    public async Task UpdateAsync_WhenNoMainBranchExists_ReturnsSkipped()
    {
      // Arrange
      var repo = _sandbox.CloneRepository("no-main");
      _sandbox.Git(repo, "remote", "remove", "origin");
      _sandbox.Git(repo, "branch", "-m", FeatureBranch);

      // Act
      var result = await _updater.UpdateAsync(repo, Options(), CancellationToken.None);

      // Assert
      result.Status.Should().Be(RepositoryStatus.Skipped);
    }

    [Fact]
    public async Task UpdateAsync_WhenAlreadyUpToDate_SkipsStashAndLeavesRepositoryUntouched()
    {
      // Arrange
      var repo = _sandbox.CloneRepository("up-to-date");
      _sandbox.Git(repo, "checkout", "-b", FeatureBranch);
      await File.WriteAllTextAsync(Path.Combine(repo, UntrackedFile), "wip", TestContext.Current.CancellationToken);

      // Act
      var result = await _updater.UpdateAsync(repo, Options(), CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      result.Status.Should().Be(RepositoryStatus.Updated);
      result.Branch.Should().Be(DefaultBranch);
      result.Commits.Should().Be(0);
      result.Stashed.Should().BeFalse();
      result.Details.Should().Contain("Already up to date");
      _sandbox.Git(repo, "branch", "--show-current").Should().Be(FeatureBranch);
      _sandbox.Git(repo, "stash", "list").Should().BeEmpty();
      File.Exists(Path.Combine(repo, UntrackedFile)).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_WhenLocalMainIsAheadOfOrigin_KeepsLocalCommitsAndReportsNoNewCommits()
    {
      // Arrange
      var repo = _sandbox.CloneRepository("ahead");
      _sandbox.Git(repo, "commit", "--allow-empty", "-m", "local only");
      var localHead = _sandbox.Git(repo, "rev-parse", "HEAD");

      // Act
      var result = await _updater.UpdateAsync(repo, Options(), CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      result.Status.Should().Be(RepositoryStatus.Updated);
      result.Commits.Should().Be(0);
      result.ToCommit.Should().Be(localHead[..8]);
      _sandbox.Git(repo, "rev-parse", "HEAD").Should().Be(localHead);
    }

    [Fact]
    public async Task UpdateAsync_WhenLocalMainHasDiverged_ReturnsErrorAndRestoresBranch()
    {
      // Arrange
      var repo = _sandbox.CloneRepository("diverged");
      _sandbox.Git(repo, "checkout", "-b", FeatureBranch);
      _sandbox.Git(repo, "checkout", DefaultBranch);
      _sandbox.Git(repo, "commit", "--allow-empty", "-m", "local only");
      _sandbox.Git(repo, "checkout", FeatureBranch);
      _sandbox.PushNewCommit("upstream change");

      // Act
      var result = await _updater.UpdateAsync(repo, Options(), CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      result.Status.Should().Be(RepositoryStatus.Error);
      result.Details.Should().Contain("Fast-forward failed");
      _sandbox.Git(repo, "branch", "--show-current").Should().Be(FeatureBranch);
    }

    [Fact]
    public async Task UpdateAsync_WhenFetchFails_ReturnsError()
    {
      // Arrange
      var repo = _sandbox.CloneRepository("offline");
      _sandbox.Git(repo, "remote", "set-url", "origin", Path.Combine(_sandbox.Root, "missing.git"));

      // Act
      var result = await _updater.UpdateAsync(repo, Options(), CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      result.Status.Should().Be(RepositoryStatus.Error);
      result.Details.Should().Contain("Fetch failed");
    }

    [Fact]
    public async Task UpdateAsync_WhenStayOnMainAndMainIsUpToDate_StillSwitchesToMain()
    {
      // Arrange
      var repo = _sandbox.CloneRepository("stay-clean");
      _sandbox.Git(repo, "checkout", "-b", FeatureBranch);

      // Act
      var result = await _updater.UpdateAsync(repo, Options(true), CancellationToken.None);

      // Assert
      using var _ = new AssertionScope();
      result.Status.Should().Be(RepositoryStatus.Updated);
      _sandbox.Git(repo, "branch", "--show-current").Should().Be(DefaultBranch);
    }

    private static ViewOptions Options(bool stayOnMain = false) =>
      new()
      {
        RootPath = string.Empty,
        MainBranchNames = ["main", "master"],
        ThrottleLimit = ThrottleLimit,
        TimeoutSeconds = TimeoutSeconds,
        StayOnMain = stayOnMain
      };
  }
}
