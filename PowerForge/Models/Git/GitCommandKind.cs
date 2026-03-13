namespace PowerForge;

/// <summary>
/// Supported typed git commands.
/// </summary>
public enum GitCommandKind
{
    /// <summary>
    /// Executes <c>git status --porcelain=2 --branch</c>.
    /// </summary>
    StatusPorcelainBranch = 0,

    /// <summary>
    /// Executes <c>git status --short --branch</c>.
    /// </summary>
    StatusShortBranch = 1,

    /// <summary>
    /// Executes <c>git status --short</c>.
    /// </summary>
    StatusShort = 2,

    /// <summary>
    /// Executes <c>git rev-parse --show-toplevel</c>.
    /// </summary>
    ShowTopLevel = 3,

    /// <summary>
    /// Executes <c>git pull --rebase</c>.
    /// </summary>
    PullRebase = 4,

    /// <summary>
    /// Executes <c>git switch -c &lt;branch&gt;</c>.
    /// </summary>
    CreateBranch = 5,

    /// <summary>
    /// Executes <c>git push --set-upstream &lt;remote&gt; &lt;branch&gt;</c>.
    /// </summary>
    SetUpstream = 6,

    /// <summary>
    /// Executes <c>git remote get-url &lt;remote&gt;</c>.
    /// </summary>
    GetRemoteUrl = 7
}
