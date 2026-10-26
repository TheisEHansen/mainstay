using FluentAssertions;
using MainStay.Services.Repository;
using Xunit;

namespace MainStay.Tests
{
  public sealed class RepositoryScannerTests : IDisposable
  {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pullmain-scan-" + Guid.NewGuid().ToString("N"));

    public RepositoryScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Find_WhenFolderContainsRepositoriesAndWorktrees_ReturnsThemAndSkipsNodeModules()
    {
      // Arrange
      const string plainRepo = "plain";
      const string worktree = "worktree";
      const string ignored = "node_modules";
      Directory.CreateDirectory(Path.Combine(_root, plainRepo, ".git"));
      Directory.CreateDirectory(Path.Combine(_root, worktree));
      File.WriteAllText(Path.Combine(_root, worktree, ".git"), "gitdir: elsewhere");
      Directory.CreateDirectory(Path.Combine(_root, ignored, "package", ".git"));

      // Act
      var repositories = RepositoryScanner.Find(_root);

      // Assert
      repositories.Should().BeEquivalentTo(Path.Combine(_root, plainRepo), Path.Combine(_root, worktree));
    }

    [Fact]
    public void Find_WhenRepositoryContainsNestedRepository_DoesNotDescendIntoIt()
    {
      // Arrange
      const string outer = "outer";
      Directory.CreateDirectory(Path.Combine(_root, outer, ".git"));
      Directory.CreateDirectory(Path.Combine(_root, outer, "vendor", "inner", ".git"));

      // Act
      var repositories = RepositoryScanner.Find(_root);

      // Assert
      repositories.Should().BeEquivalentTo(Path.Combine(_root, outer));
    }

    [Fact]
    public void Find_WhenBuildFoldersExist_SkipsThemUnlessTheyAreRepositories()
    {
      // Arrange
      const string project = "project";
      const string binRepo = "bin";
      Directory.CreateDirectory(Path.Combine(_root, project, "bin", "tool", ".git"));
      Directory.CreateDirectory(Path.Combine(_root, project, "obj", "tool", ".git"));
      Directory.CreateDirectory(Path.Combine(_root, binRepo, ".git"));

      // Act
      var repositories = RepositoryScanner.Find(_root);

      // Assert
      repositories.Should().BeEquivalentTo(Path.Combine(_root, binRepo));
    }
  }
}
