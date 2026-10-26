using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;
using MainStay.Models;
using MainStay.Models.Repository;
using MainStay.Services.Git;
using MainStay.Services.Repository;

namespace MainStay.Services
{
  public sealed class Runner
  {
    public async Task<IReadOnlyList<RepositoryResult>> RunAsync(ViewOptions viewOptions, IProgress<RunProgress> progress, CancellationToken cancellationToken)
    {
      ArgumentNullException.ThrowIfNull(viewOptions);
      ArgumentNullException.ThrowIfNull(progress);

      var updater = new RepositoryUpdater(new GitClient(TimeSpan.FromSeconds(viewOptions.TimeoutSeconds)));
      var results = new ConcurrentBag<RepositoryResult>();

      var discovered = 0;
      var completed = 0;

      // Scanning and updating overlap: repositories are updated as soon as the scanner finds them.
      var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
      {
        SingleWriter = true
      });

      using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

      var scan = Task.Run(() =>
      {
        try
        {
          foreach (var repoPath in RepositoryScanner.Enumerate(viewOptions.RootPath))
          {
            _ = channel.Writer.TryWrite(repoPath);
            progress.Report(new RunProgress(Volatile.Read(ref completed), Interlocked.Increment(ref discovered), null));
          }

          _ = channel.Writer.TryComplete();
        }
        catch (Exception exception)
        {
          _ = channel.Writer.TryComplete(exception);
        }
      }, scanCancellation.Token);

      var parallelOptions = new ParallelOptions
      {
        MaxDegreeOfParallelism = viewOptions.ThrottleLimit,
        CancellationToken = cancellationToken
      };

      try
      {
        await Parallel.ForEachAsync(channel.Reader.ReadAllAsync(cancellationToken), parallelOptions,
          async (repoPath, token) =>
          {
            RepositoryResult result;
            try
            {
              result = await updater.UpdateAsync(repoPath, viewOptions, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
              result = new RepositoryResult
              {
                Repository = Path.GetFileName(repoPath),
                Path = repoPath,
                Status = RepositoryStatus.Error,
                Details = exception.Message
              };
            }

            results.Add(result);
            progress.Report(new RunProgress(Interlocked.Increment(ref completed), Volatile.Read(ref discovered), result));
          });
      }
      finally
      {
        await scanCancellation.CancelAsync();
        await scan;
      }

      return [.. results.OrderBy(result => result.Repository, StringComparer.OrdinalIgnoreCase)];
    }
  }
}
