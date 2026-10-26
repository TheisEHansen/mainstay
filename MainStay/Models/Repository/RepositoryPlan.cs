namespace MainStay.Models.Repository
{
  public sealed record RepositoryPlan
  {
    public required string Branch { get; init; }

    public required string OriginalRef { get; init; }

    public required bool NeedsSwitch { get; init; }

    public required bool StayOnMain { get; init; }
  }
}
