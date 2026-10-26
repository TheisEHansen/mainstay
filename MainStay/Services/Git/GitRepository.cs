using MainStay.Models.Git;

namespace MainStay.Services.Git
{
  public sealed class GitRepository(GitClient gitClient, string path)
  {
    private const string LocalPrefix = "refs/heads/";
    private const string RemotePrefix = "refs/remotes/origin/";

    public Task<GitResult> FetchAsync(CancellationToken cancellationToken) => RunAsync(["fetch", "--quiet", "--no-tags", "--no-recurse-submodules", "origin"], cancellationToken);
    public Task<GitResult> CheckoutAsync(string reference, CancellationToken cancellationToken) => RunAsync(["checkout", reference], cancellationToken);
    public Task<GitResult> FastForwardAsync(string branch, CancellationToken cancellationToken) => RunAsync(["merge", "--ff-only", $"origin/{branch}"], cancellationToken);
    public Task<GitResult> StashAsync(string message, CancellationToken cancellationToken) => RunAsync(["stash", "push", "--include-untracked", "--message", message], cancellationToken);
    public Task<GitResult> StashPopAsync(CancellationToken cancellationToken) => RunAsync(["stash", "pop"], cancellationToken);

    public async Task<string?> GetCurrentBranchAsync(CancellationToken cancellationToken)
    {
      var result = await RunAsync(["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);
      return result is { Success: true, Stdout.Length: > 0 } ? result.Stdout : null;
    }

    public async Task<string> GetHeadCommitAsync(CancellationToken cancellationToken) => (await RunAsync(["rev-parse", "HEAD"], cancellationToken)).Stdout;

    public async Task<bool> IsDirtyAsync(CancellationToken cancellationToken)
    {
      var result = await RunAsync(["status", "--porcelain", "--untracked-files=normal"], cancellationToken);
      return result.Stdout.Length > 0;
    }

    public async Task<int> CountCommitsAsync(string fromCommit, string toCommit, CancellationToken cancellationToken)
    {
      if (fromCommit.Length == 0 || toCommit.Length == 0 || fromCommit == toCommit)
      {
        return 0;
      }

      var result = await RunAsync(["rev-list", "--count", $"{fromCommit}..{toCommit}"], cancellationToken);
      return result.Success && int.TryParse(result.Stdout, out var count) ? count : 0;
    }

    // One for-each-ref call yields local branches, origin branches, their commits and origin/HEAD's target.
    public async Task<RefSnapshot> GetRefsAsync(CancellationToken cancellationToken)
    {
      var result = await RunAsync(["for-each-ref", "--format=%(refname)%09%(objectname)%09%(symref)", "refs/heads", "refs/remotes/origin"], cancellationToken);

      var local = new Dictionary<string, string>(StringComparer.Ordinal);
      var remote = new Dictionary<string, string>(StringComparer.Ordinal);
      string? defaultBranch = null;

      foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
      {
        // The trailing symref field is empty for most refs, so it may have been trimmed away.
        var fields = line.TrimEnd('\r').Split('\t');
        if (fields.Length < 2)
        {
          continue;
        }

        var refName = fields[0];
        var commit = fields[1];
        var symref = fields.Length > 2 ? fields[2] : string.Empty;

        if (refName.StartsWith(LocalPrefix, StringComparison.Ordinal))
        {
          local[refName[LocalPrefix.Length..]] = commit;
        }
        else if (refName == RemotePrefix + "HEAD")
        {
          if (symref.StartsWith(RemotePrefix, StringComparison.Ordinal))
          {
            defaultBranch = symref[RemotePrefix.Length..];
          }
        }
        else if (refName.StartsWith(RemotePrefix, StringComparison.Ordinal))
        {
          remote[refName[RemotePrefix.Length..]] = commit;
        }
      }

      return new RefSnapshot(local, remote, defaultBranch);
    }

    private Task<GitResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => gitClient.RunAsync(path, arguments, cancellationToken);
  }

  public sealed record RefSnapshot(IReadOnlyDictionary<string, string> Local, IReadOnlyDictionary<string, string> Remote, string? DefaultBranch)
  {
    public string? ResolveMainBranch(IReadOnlyList<string> candidates)
    {
      if (DefaultBranch != null && (Local.ContainsKey(DefaultBranch) || Remote.ContainsKey(DefaultBranch)))
      {
        return DefaultBranch;
      }

      return candidates.FirstOrDefault(Local.ContainsKey) ?? candidates.FirstOrDefault(Remote.ContainsKey);
    }

    public bool IsUpToDate(string branch) =>
      Local.TryGetValue(branch, out var localCommit)
      && Remote.TryGetValue(branch, out var remoteCommit)
      && localCommit == remoteCommit;
  }
}
