namespace MainStay.Models.Repository
{
  public sealed record RepositoryOutcome
  {
    public int Commits { get; init; }

    public string? FromCommit { get; init; }

    public string? ToCommit { get; init; }

    public string Output { get; init; } = string.Empty;

    public string? Failure { get; init; }
  }
}
