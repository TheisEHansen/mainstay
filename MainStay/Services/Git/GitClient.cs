using System.Diagnostics;
using MainStay.Models.Git;

namespace MainStay.Services.Git
{
  public sealed class GitClient(TimeSpan timeout)
  {
    public async Task<GitResult> RunAsync(string repoPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
      var startInfo = new ProcessStartInfo("git")
      {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
      };

      startInfo.ArgumentList.Add("-C");
      startInfo.ArgumentList.Add(repoPath);

      foreach (var argument in arguments)
      {
        startInfo.ArgumentList.Add(argument);
      }

      // Fail fast instead of hanging on a credential prompt nobody can answer.
      startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
      // Read-only commands such as status must not take index locks that contend with parallel git calls.
      startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

      using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
      using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeoutSource.CancelAfter(timeout);

      try
      {
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        await process.WaitForExitAsync(timeoutSource.Token);

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();
        var output = string.Join(Environment.NewLine, new[]
        {
          stdout,
          stderr
        }.Where(text => text.Length > 0));

        return new GitResult(process.ExitCode == 0, stdout, output);
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        return new GitResult(false, string.Empty, $"Timed out after {timeout.TotalSeconds:0} seconds: git {string.Join(' ', arguments)}");
      }
      finally
      {
        if (!process.HasExited)
        {
          process.Kill(true);
        }
      }
    }
  }
}
