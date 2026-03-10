namespace PowerForgeStudio.Domain.Portfolio;

public enum RepositoryGitDiagnosticCode
{
    GitUnavailable = 0,
    DetachedHead = 1,
    NoUpstream = 2,
    DirtyWorkingTree = 3,
    BehindUpstream = 4,
    ProtectedBaseBranchFlow = 5
}
