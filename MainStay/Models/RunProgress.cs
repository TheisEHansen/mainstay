using MainStay.Models.Repository;

namespace MainStay.Models
{
  public sealed record RunProgress(int Completed, int Total, RepositoryResult? Latest);
}
