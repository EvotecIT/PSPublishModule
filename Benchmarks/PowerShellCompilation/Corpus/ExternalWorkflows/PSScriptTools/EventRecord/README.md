# Borrowed Windows event records

`Convert-EventLogRecord.ps1` is unchanged from PSScriptTools revision
`549fa3e3d769532320054fc0b3c8f42df0452361`, under the accompanying MIT license.
Its SHA-256 is `0a16c0e80ff0780ae2a16bd4331efb409f625b43d92b2f4e0a6310d9ca77fb9a`.
The license SHA-256 is
`fb7d4eb5de8741978ce3dc1338771a53756e87d1134dc19b0199b2ee43beef95`.

Run `Observe.ps1 -OriginalModule <original.psm1> -GeneratedModule <generated.psd1>`
in an isolated Windows PowerShell 5.1 or PowerShell 7 process with read access
to at least two System log events. Both modules must contain the pinned function.
The observer reads two real records once and supplies those same objects to
both implementations. Twelve cases compare arrays, scalar and pipeline input,
duplicates, actual verbose records, binding errors, downstream stop and disposed
records. Mixed valid/disposed input preserves the first output before termination
under Continue, SilentlyContinue and Stop. The authored ToXml failure terminates
in all three cases; the observer does not assume ErrorAction makes it recoverable.

`ObserveBorrowed.ps1` additionally requires Read-BorrowedEvent,
Read-BorrowedEvents, Read-BorrowedEventList and Read-BorrowedEventMap from the
focused EventRecordData fixture in both modules. Eight cases compare reference
identity, typed containers, binding metadata, null/invalid input, pipeline and
downstream stop. These synthetic functions add no external workload coverage.

The observers compare projected outputs and error source positions. Event
payloads and verbose messages are hashed in memory; raw event data is not written
to evidence. They dispose only their own records in finally. Neither the compiler
nor the generated function takes ownership of caller handles. No log writes,
export, network or administrative operation runs.

The qualified artifacts emit the unchanged function on Hybrid net10.0/PowerShell
7 and net472/Windows PowerShell 5.1. Exact EventLogRecord scalar, array and supported
container parameters require both host-type and native-function capabilities.
Strict and either missing capability remain closed. EventRecord base types,
record construction, other providers, CIM, non-Windows execution, full-module
execution, performance and reviewed dependency locks are outside this proof.
