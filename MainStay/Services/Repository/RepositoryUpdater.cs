using System.IO;
using MainStay.Models;
using MainStay.Models.Repository;
using MainStay.Services.Git;

namespace MainStay.Services.Repository
{
  public sealed class RepositoryUpdater(GitClient gitClient)
  {
    public async Task<RepositoryResult> UpdateAsync(string repoPath, ViewOptions viewOptions, CancellationToken cancellationToken)
    {
      var started = TimeProvider.System.GetTimestamp();
      var result = await UpdateCoreAsync(repoPath, viewOptions, cancellationToken);
      return result with
      {
        Duration = TimeProvider.System.GetElapsedTime(started)
      };
    }

    private async Task<RepositoryResult> UpdateCoreAsync(string repoPath, ViewOptions viewOptions, CancellationToken cancellationToken)
    {
      var repo = new GitRepository(gitClient, repoPath);
      var name = Path.GetFileName(repoPath);

      var originalBranch = await repo.GetCurrentBranchAsync(cancellationToken);
      var fetch = await repo.FetchAsync(cancellationToken);
      var refs = await repo.GetRefsAsync(cancellationToken);

      var mainBranch = refs.ResolveMainBranch(viewOptions.MainBranchNames);
      if (mainBranch is null)
      {
        return new RepositoryResult
        {
          Repository = name,
          Path = repoPath,
          Status = RepositoryStatus.Skipped,
          Details = $"Could not determine main branch (tried: {string.Join(", ", viewOptions.MainBranchNames)})."
        };
      }

      var result = new RepositoryResult
      {
        Repository = name,
        Path = repoPath,
        Status = RepositoryStatus.Updated,
        Branch = mainBranch
      };

      // A stale origin/<main> would make the up-to-date check below lie, so a failed fetch is an error.
      if (!fetch.Success)
      {
        return result with
        {
          Status = RepositoryStatus.Error,
          Details = $"Fetch failed: {fetch.Output}"
        };
      }

      var needsSwitch = originalBranch != mainBranch;

      // Nothing to pull, so no stash/checkout round trip is needed. StayOnMain still has to switch.
      if (refs.IsUpToDate(mainBranch) && !(needsSwitch && viewOptions.StayOnMain))
      {
        var head = ShortCommit(refs.Remote[mainBranch]);
        return result with
        {
          FromCommit = head,
          ToCommit = head,
          Details = "Already up to date."
        };
      }

      // Detached HEAD: remember the commit so the repository can be put back exactly as found.
      var originalRef = originalBranch ?? await repo.GetHeadCommitAsync(cancellationToken);
      var plan = new RepositoryPlan
      {
        Branch = mainBranch,
        OriginalRef = originalRef,
        NeedsSwitch = needsSwitch,
        StayOnMain = viewOptions.StayOnMain
      };

      return await PullAndRestoreAsync(repo, plan, result, cancellationToken);
    }

    private static async Task<RepositoryResult> PullAndRestoreAsync(GitRepository repo, RepositoryPlan repositoryPlan, RepositoryResult result, CancellationToken cancellationToken)
    {
      var stashed = await TryStashAsync(repo, repositoryPlan.Branch, cancellationToken);
      if (stashed is null)
      {
        return result with
        {
          Status = RepositoryStatus.Error,
          Details = "Failed to stash local changes."
        };
      }

      RepositoryOutcome repository;
      RestoreOutcome restore;
      try
      {
        repository = await PullAsync(repo, repositoryPlan, cancellationToken);
      }
      finally
      {
        // CancellationToken.None: restoring must still happen when the user cancelled the run.
        restore = await RestoreAsync(repo, repositoryPlan, stashed.Value, CancellationToken.None);
      }

      result = result with
      {
        Stashed = stashed.Value,
        Commits = repository.Commits,
        FromCommit = repository.FromCommit,
        ToCommit = repository.ToCommit
      };

      string?[] failures = [repository.Failure, .. restore.Failures];
      var errors = failures.OfType<string>().ToList();
      if (errors.Count > 0)
      {
        return result with
        {
          Status = RepositoryStatus.Error,
          Details = string.Join(' ', errors)
        };
      }

      return result with
      {
        Details = string.Join(' ', new[]
        {
          repository.Output,
          restore.Note
        }.Where(text => text.Length > 0))
      };
    }

    // Returns null when stashing failed, otherwise whether anything was stashed.
    private static async Task<bool?> TryStashAsync(GitRepository repo, string mainBranch, CancellationToken cancellationToken)
    {
      if (!await repo.IsDirtyAsync(cancellationToken))
      {
        return false;
      }

      var message = $"Auto-stash before switching to {mainBranch} on {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
      var stash = await repo.StashAsync(message, cancellationToken);
      return stash.Success ? true : null;
    }

    private static async Task<RepositoryOutcome> PullAsync(GitRepository repo, RepositoryPlan repositoryPlan, CancellationToken cancellationToken)
    {
      if (repositoryPlan.NeedsSwitch)
      {
        var checkout = await repo.CheckoutAsync(repositoryPlan.Branch, cancellationToken);
        if (!checkout.Success)
        {
          return Failed($"Failed to checkout {repositoryPlan.Branch}: {checkout.Output}");
        }
      }

      var fromCommit = await repo.GetHeadCommitAsync(cancellationToken);
      var merge = await repo.FastForwardAsync(repositoryPlan.Branch, cancellationToken);
      if (!merge.Success)
      {
        return Failed($"Fast-forward failed: {merge.Output}");
      }

      var toCommit = await repo.GetHeadCommitAsync(cancellationToken);
      var commits = await repo.CountCommitsAsync(fromCommit, toCommit, cancellationToken);
      return new RepositoryOutcome
      {
        Commits = commits,
        FromCommit = ShortCommit(fromCommit),
        ToCommit = ShortCommit(toCommit),
        Output = merge.Output
      };
    }

    // Always runs, so a failure never strands the repository on main or leaves a stash behind.
    // With StayOnMain the stash is kept: popping it onto main would apply another branch's work in progress.
    private static async Task<RestoreOutcome> RestoreAsync(GitRepository repo, RepositoryPlan repositoryPlan, bool stashed, CancellationToken cancellationToken)
    {
      if (repositoryPlan is { NeedsSwitch: true, StayOnMain: true })
      {
        return stashed
          ? new RestoreOutcome($"Left on {repositoryPlan.Branch}; changes from '{repositoryPlan.OriginalRef}' kept in stash (git stash pop after switching back).", [])
          : new RestoreOutcome(string.Empty, []);
      }

      var failures = new List<string>();

      if (repositoryPlan.NeedsSwitch)
      {
        var checkoutBack = await repo.CheckoutAsync(repositoryPlan.OriginalRef, cancellationToken);
        if (!checkoutBack.Success)
        {
          failures.Add($"Failed to switch back to {repositoryPlan.OriginalRef}: {checkoutBack.Output}");
          if (stashed)
          {
            failures.Add("Local changes remain stashed.");
          }

          return new RestoreOutcome(string.Empty, failures);
        }
      }

      if (stashed)
      {
        var pop = await repo.StashPopAsync(cancellationToken);
        if (!pop.Success)
        {
          failures.Add($"Failed to restore stashed changes (still in stash): {pop.Output}");
        }
      }

      return new RestoreOutcome(string.Empty, failures);
    }

    private static RepositoryOutcome Failed(string failure) =>
      new()
      {
        Failure = failure
      };

    private static string ShortCommit(string commit) => commit.Length > 8 ? commit[..8] : commit;
  }
}
