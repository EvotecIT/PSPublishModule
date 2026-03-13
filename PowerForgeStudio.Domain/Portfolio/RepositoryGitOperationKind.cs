namespace PowerForgeStudio.Domain.Portfolio;

public enum RepositoryGitOperationKind
{
    StatusShortBranch = 0,
    StatusShort = 1,
    ShowTopLevel = 2,
    PullRebase = 3,
    CreateBranch = 4,
    SetUpstream = 5
}
