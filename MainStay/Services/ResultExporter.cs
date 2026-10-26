using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MainStay.Models.Repository;

namespace MainStay.Services
{
  public static class ResultExporter
  {
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
      WriteIndented = true,
      Converters =
      {
        new JsonStringEnumConverter()
      }
    };

    public static async Task ExportAsync(string path, IReadOnlyList<RepositoryResult> results, CancellationToken cancellationToken)
    {
      var content = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? JsonSerializer.Serialize(results, JsonOptions) : ToCsv(results);
      await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken);
    }

    private static string ToCsv(IReadOnlyList<RepositoryResult> results)
    {
      var builder = new StringBuilder();
      builder.AppendLine("Repository,Path,Branch,Status,Stashed,Commits,FromCommit,ToCommit,DurationSeconds,Details");
      foreach (var result in results)
      {
        string[] fields =
        [
          result.Repository,
          result.Path,
          result.Branch ?? string.Empty,
          result.Status.ToString(),
          result.Stashed.ToString(),
          result.Commits.ToString(CultureInfo.InvariantCulture),
          result.FromCommit ?? string.Empty,
          result.ToCommit ?? string.Empty,
          result.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),
          result.Details
        ];
        builder.AppendLine(string.Join(',', fields.Select(Quote)));
      }

      return builder.ToString();
    }

    private static string Quote(string field) => $"\"{field.Replace("\"", "\"\"")}\"";
  }
}
