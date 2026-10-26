using System.IO;

namespace MainStay.Services.Repository
{
  public static class RepositoryScanner
  {
    private static readonly string[] SkippedFolders = ["node_modules", "bin", "obj", ".vs", ".idea"];

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
      IgnoreInaccessible = true,
      AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static IReadOnlyList<string> Find(string rootPath)
    {
      var repositories = Enumerate(rootPath).ToList();
      repositories.Sort(StringComparer.OrdinalIgnoreCase);
      return repositories;
    }

    public static IEnumerable<string> Enumerate(string rootPath)
    {
      var pending = new Stack<string>();
      var root = Path.GetFullPath(rootPath);
      pending.Push(root);

      while (pending.Count > 0)
      {
        var current = pending.Pop();

        // '.git' is a file (not a directory) for worktrees and submodules.
        var gitPath = Path.Combine(current, ".git");
        if (Directory.Exists(gitPath) || File.Exists(gitPath))
        {
          yield return current;
          continue;
        }

        if (current != root && SkippedFolders.Contains(Path.GetFileName(current), StringComparer.OrdinalIgnoreCase))
        {
          continue;
        }

        // Materialised so a folder that vanishes or is locked mid-scan is skipped instead of aborting the run.
        string[] children;
        try
        {
          children = Directory.GetDirectories(current, "*", EnumerationOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
          continue;
        }

        foreach (var directory in children)
        {
          pending.Push(directory);
        }
      }
    }
  }
}
