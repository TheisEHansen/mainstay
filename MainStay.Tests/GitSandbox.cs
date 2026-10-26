using System.Diagnostics;

namespace MainStay.Tests
{
  // Builds a bare "origin" with a 'develop' default branch, plus helpers to clone it and push new commits.
  public sealed class GitSandbox : IDisposable
  {
    private readonly string _origin;
    private readonly string _upstreamClone;

    public GitSandbox()
    {
      Root = Path.Combine(Path.GetTempPath(), "pullmain-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Root);

      _origin = Path.Combine(Root, "origin.git");
      _upstreamClone = Path.Combine(Root, "upstream");
      Git(Root, "init", "--bare", "-b", "develop", _origin);
      Git(Root, "clone", _origin, _upstreamClone);
      Commit(_upstreamClone, "initial");
      Git(_upstreamClone, "push", "origin", "develop");
    }

    public string Root { get; }

    public string CloneRepository(string name)
    {
      var path = Path.Combine(Root, "repos", name);
      Git(Root, "clone", _origin, path);
      return path;
    }

    public void PushNewCommit(string message)
    {
      Commit(_upstreamClone, message);
      Git(_upstreamClone, "push", "origin", "develop");
    }

    public string Git(string workingDirectory, params string[] arguments)
    {
      var startInfo = new ProcessStartInfo("git")
      {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true
      };
      startInfo.ArgumentList.Add("-c");
      startInfo.ArgumentList.Add("user.email=test@example.com");
      startInfo.ArgumentList.Add("-c");
      startInfo.ArgumentList.Add("user.name=Test");
      foreach (var argument in arguments)
      {
        startInfo.ArgumentList.Add(argument);
      }

      using var process = Process.Start(startInfo)!;
      var stdoutTask = process.StandardOutput.ReadToEndAsync();
      var stderrTask = process.StandardError.ReadToEndAsync();
      process.WaitForExit();
      if (process.ExitCode != 0)
      {
        throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderrTask.Result}");
      }

      return stdoutTask.Result.Trim();
    }

    public void Dispose()
    {
      foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
      {
        File.SetAttributes(file, FileAttributes.Normal);
      }

      Directory.Delete(Root, true);
    }

    private void Commit(string repoPath, string message) => Git(repoPath, "commit", "--allow-empty", "-m", message);
  }
}
