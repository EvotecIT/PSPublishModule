# PowerShellForGitHub invocation metadata fixture

`Write-InvocationLog.ps1` is the unchanged function extent from Microsoft's MIT-licensed `PowerShellForGitHub` `Helpers.ps1` at commit `fd4fa65f23ca30f18f85312a85348e660c287a9c`. The pinned source archive SHA-256 is `f5b7dae55fdd932f4460762e794849fe1f1e6c6083e36509dfbcba676ce7fb15`; the extracted function SHA-256 is `ceffa801f781837e6991f89527b799c341a66ca6a3b3a7c9c08d71ff838f8deb`. The upstream license is copied as `LICENSE`.

The artifact test adds fixture-owned logging policy and an in-memory `Write-Log` provider after this unchanged function. It calls the function with explicit and default caller `InvocationInfo`, redaction and exclusion options, and invalid binding on PowerShell 7/net10 and Windows PowerShell 5.1/net472. No GitHub request or file log is made. This fixture does not establish full-module import or network behavior.
