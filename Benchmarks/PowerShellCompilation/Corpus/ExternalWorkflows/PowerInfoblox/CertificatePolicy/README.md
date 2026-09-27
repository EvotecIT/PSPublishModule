# PowerInfoblox certificate policy: hosted boundary

`Prepare.ps1` assembles an unchanged, SHA-256-pinned `Hide-SelfSignedCerts` source into an isolated module. Supply the external PowerInfoblox source and a new task-owned directory. The fixture contains no translated method; it qualifies preservation of a hosted function in a generated Hybrid artifact.

Run `Observe.ps1` in a fresh, noninteractive child process for each artifact, host and case. `real` executes the authored `Add-Type` on Windows PowerShell 5.1 and calls the installed policy with null arguments. `failure` shadows `Add-Type` only inside the imported module to exercise its warning and state-preservation path. Both cases call the function twice. PowerShell 7 must return before either type-definition path and set only the module configuration flag. The observer asserts these contracts and restores the previous process-local policy in `finally`.

No HTTP request, TLS handshake, certificate store change or machine configuration change is part of this workflow. Do not run the observer in an existing interactive session. This proof does not endorse disabling certificate validation and does not qualify arbitrary runtime-generated C#, P/Invoke, portable certificate APIs or a complete PowerInfoblox module workflow. Strict rejection remains required on both targets.
