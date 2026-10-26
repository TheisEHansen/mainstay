namespace MainStay.Models
{
  public sealed record ViewOptions
  {
    public required string RootPath { get; init; }

    public required IReadOnlyList<string> MainBranchNames { get; init; }

    public required int ThrottleLimit { get; init; }

    public required int TimeoutSeconds { get; init; }

    public required bool StayOnMain { get; init; }
  }
}
