namespace MainStay.Models.Repository
{
  public sealed record RepositoryResult
  {
    public required string Repository { get; init; }

    public required string Path { get; init; }

    public required RepositoryStatus Status { get; init; }

    public string? Branch { get; init; }

    public bool Stashed { get; init; }

    public int Commits { get; init; }

    public string? FromCommit { get; init; }

    public string? ToCommit { get; init; }

    public TimeSpan Duration { get; init; }

    public string Details { get; init; } = string.Empty;
  }
}
