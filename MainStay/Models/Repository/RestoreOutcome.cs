namespace MainStay.Models.Repository
{
  public sealed record RestoreOutcome(string Note, IReadOnlyList<string> Failures);
}
