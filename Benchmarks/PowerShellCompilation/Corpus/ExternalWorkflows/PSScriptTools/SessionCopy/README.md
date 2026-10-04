# Offline session-copy workflow

`Copy-PSFunction.ps1` is unchanged from PSScriptTools revision
`549fa3e3d769532320054fc0b3c8f42df0452361`, under the accompanying MIT license.
Source SHA-256: `3b527d3e31221935e8c7c7f40ad3ffd0eb12eb194e02f4aeed8d65b9ecdbbdda`.

`Observe.ps1` is a compiler qualification driver. It creates an unopened,
caller-owned session object and replaces function lookup and remote invocation
with module-owned in-memory providers. A refusing global provider catches
unexpected invocation scope. The remote script block is inspected but never
executed; no connection opens and no function drive is changed.

The driver compares direct and pipeline input, missing functions, provider
errors, error preferences, session identity and downstream stopping. This proves
the wrapper's offline composition and native session binding. It does not
qualify real remote installation, transport, authentication or reconnection.
