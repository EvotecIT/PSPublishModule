# Signing provider preparation

`powerforge signing prepare` signs one generated local proof before a longer release workflow needs the configured provider. Enter any PIN or consent only in the provider's normal secure dialog. The command never accepts a PIN and does not publish packages or change provider policy.

```powershell
powerforge signing prepare --thumbprint $approvedSigningThumbprint --out $newLocalReceiptDirectory
```

Use the same explicitly approved certificate as the intended release. The Windows current-user certificate store, installed PSPublishModule signing engine and configured timestamp service perform the operation. `--module-path` selects an installed manifest when normal module discovery is unsuitable; relative filesystem paths resolve from the caller's working directory. `--timestamp-url` selects the existing HTTP(S) timestamp service. No certificate-search fallback or signing retry is performed.

The output directory must be new and on a local fixed disk, outside a source checkout. The command executes its helper in a unique local operation directory, signs an exact generated file through the shared engine, verifies its signer and Authenticode status, and checks the verified-byte hash before retaining `signing-proof.ps1` and `receipt.json`. Existing destination content is preserved. A failed operation does not write a success receipt. `--output json` keeps stdout as one JSON envelope and sends the interaction notice to stderr.

A successful receipt says that this signature was verified at its recorded time. It does not establish a cache duration or guarantee another process can sign without a prompt. Locking Windows, removing the token, a provider timeout or another provider policy may require a fresh interaction. The command does not keep a session alive, change auto-lock, store credentials or authorize a later release. Those releases retain their own source, review, signing and publication gates.
