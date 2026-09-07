# Pinned conversion workflows

`Convert-BinaryToString.ps1` is the unchanged complete function from [PSSharedGoods commit 2a807a4](https://github.com/EvotecIT/PSSharedGoods/blob/2a807a4f11ba458b7bc405ce3674d93838639af1/Public/Converts/Convert-BinaryToString.ps1). Its MIT license is included in this directory.

The source file SHA-256 is `8d237e02921013b114a7d0af851e8369e2693bbfad5a8d5a17b8ee30e48fa332`. The compiler discovery packet pins the containing archive separately.

`CompleteWorkflow_PinnedBinaryToStringPreservesBindingAndOutput` builds the complete function through the normal artifact builder and compares the original and generated command. It covers null, empty, singleton, malformed, Unicode, nested and invalid input; named, alias, positional and pipeline binding; record types and order; error identity and capture; and a later successful invocation. This fixture exercises an in-memory conversion and performs no administration operations.

Additional probes compare traced input enumerators that fail during `MoveNext`, `Current`, or disposal. This covers parameter-binding enumeration; it does not qualify arbitrary enumeration inside compiled bodies.

Qualification of this function does not imply qualification of the rest of PSSharedGoods or of a runtime-free library target.

`Convert-HexToBinary.ps1` is the unchanged function from the same commit, with SHA-256 `4ede6b109b121813150cdce0b515964127183c2feedd7e30d8d9c86737194a85`. `CompleteWorkflow_PinnedHexPreservesCaptureErrorsAndContainers` qualifies the complete command artifact in Strict and Hybrid modes on Windows PowerShell 5.1, PowerShell 7.4.18, and PowerShell 7.6.5. It compares 132 cases per configuration: exact result containers and serialization, valid and malformed pairs, odd lengths, partial parsing errors, named/positional/pipeline binding, empty input, downstream stop, and later successful invocation.

The complete CLR method contains the loop, capture, numeric indexing, and parsing. Its final unqualified `Write-Output -NoEnumerate` remains one explicitly hosted command region so runtime command lookup is preserved. No whole authored function is retained. This command-artifact qualification does not establish a runtime-free library contract.
