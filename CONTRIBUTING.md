# Contributing

This is original MIT software by Lofrano Tecnologia, development version 0.1.0. Contributions must be original or explicitly license-compatible. Do not contribute private client code, credentials, real directory dumps or personal data.

Prepare dependencies using the README Dockerfile or an already prepared Ubuntu 24.04/.NET 10 environment. Restore dependencies separately, retain genuine generated NuGet locks, then run:

```sh
dotnet build MembershipEvidence.sln --no-restore --configuration Release
dotnet test --solution MembershipEvidence.sln --no-restore --configuration Release
dotnet run --no-restore --project src/MembershipEvidence.Cli -- demo --assert
```

Run runtime validation unprivileged with loopback available and no external network. Do not skip native integration tests. Avoid warning suppression, invented lock hashes, runtime downloads and tests that merely restate implementation internals.

Changes to error codes, completeness, path ordering, request accounting, schema qualification, guard semantics or JSON require updating docs/contract.md and adding regression tests with independently understandable expected results. Changes to process ownership must preserve exclusive direct-child reaping, reset SIGCHLD before fork, and prohibit any signal after reap. Never introduce a numeric-PID kill fallback or an arbitrary command/endpoint API.

Review security-sensitive changes against SECURITY.md. Report vulnerabilities privately rather than attaching raw directory/tool diagnostics to public issues. For general organizational contact, use [the official homepage](https://lofranotecnologia.com.br).
