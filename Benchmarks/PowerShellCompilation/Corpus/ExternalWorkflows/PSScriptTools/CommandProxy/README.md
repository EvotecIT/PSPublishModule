# PSScriptTools command proxy workflow

`Copy-Command.ps1` is unchanged from PSScriptTools revision
`549fa3e3d769532320054fc0b3c8f42df0452361`, path `functions/Copy-Command.ps1`.
The upstream MIT license is included. The source SHA-256 is
`9fe8dae88fac427c5950685a3ca33f13cbaeb0df9b0a3df8e8968f0848831728`.

`PowerShellCompilationCommandProxyTests` builds original/generated Hybrid modules
and runs the public function against child-process-owned local commands on
PowerShell 7/net10.0 and Windows PowerShell 5.1/net472. It compares generated
source and executes the resulting proxy functions, covering defaults, parameter
aliases, pipeline input, throws, command aliases, rebinding and missing commands.
The additional metadata probe checks caller and PSBoundParameters identity.

The probe uses forward help and the console host. Dynamic parameters, alternate
help generation, ISE/VS Code editor integration and the two private helper
functions are not claimed as executed workflows. No external commands, network
connections or remote sessions are used. SDK operations remain host-backed;
this is not runtime-free PowerShell translation.
