using FluentAssertions;
using MainStay.Models.Repository;
using MainStay.Services;
using Xunit;

namespace MainStay.Tests
{
  public sealed class ResultExporterTests : IDisposable
  {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pullmain-export-" + Guid.NewGuid().ToString("N"));

    public ResultExporterTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public async Task ExportAsync_WhenPathIsCsv_QuotesFieldsContainingQuotesAndCommas()
    {
      // Arrange
      const string details = "said \"hi\", twice";
      const string expectedField = "\"said \"\"hi\"\", twice\"";
      var path = Path.Combine(_directory, "results.csv");
      var results = new[]
      {
        new RepositoryResult
        {
          Repository = "repo",
          Path = @"C:\repo",
          Status = RepositoryStatus.Error,
          Details = details
        }
      };

      // Act
      await ResultExporter.ExportAsync(path, results, CancellationToken.None);

      // Assert
      (await File.ReadAllTextAsync(path, CancellationToken.None)).Should().Contain(expectedField);
    }

    [Fact]
    public async Task ExportAsync_WhenPathIsJson_WritesStatusAsString()
    {
      // Arrange
      const string expectedStatus = "\"Skipped\"";
      var path = Path.Combine(_directory, "results.json");
      var results = new[]
      {
        new RepositoryResult
        {
          Repository = "repo",
          Path = @"C:\repo",
          Status = RepositoryStatus.Skipped
        }
      };

      // Act
      await ResultExporter.ExportAsync(path, results, CancellationToken.None);

      // Assert
      (await File.ReadAllTextAsync(path, CancellationToken.None)).Should().Contain(expectedStatus);
    }
  }
}
