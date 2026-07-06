# Copilot instructions for OpcUaAddressSpaceChecker

> Status: this repository is a scaffolded **.NET 10 global tool** project with a
> single source project at `src\OpcUaAddressSpaceChecker\OpcUaAddressSpaceChecker.csproj`.
> Tests are not scaffolded yet. Keep this file updated as real code, build, and test workflows land.

## Project intent

`OpcUaAddressSpaceChecker` is intended to inspect / validate an **OPC UA server's
address space** (nodes, references, types, namespaces). Treat OPC UA domain
correctness as a first-class concern, not an afterthought.

## Tech stack

- Language/runtime: **.NET** (the `.gitignore` is derived from GitHub's
  `Dotnet.gitignore`).
- OPC UA client work is expected to use the OPC Foundation **UA-.NETStandard**
  stack (`OPCFoundation.NetStandard.Opc.Ua.*` NuGet packages).

## Build / test / run

Canonical commands:

```sh
dotnet restore
dotnet build src\OpcUaAddressSpaceChecker\OpcUaAddressSpaceChecker.csproj
dotnet test                       # full test suite once tests exist
dotnet run --project src\OpcUaAddressSpaceChecker -- --help
```

Run a single test (choose per test framework in use):

```sh
# xUnit
dotnet test --filter "FullyQualifiedName~Namespace.ClassName.TestMethod"
# NUnit / MSTest
dotnet test --filter "Name=TestMethod"
```

When adding a test project, prefer the `.gitignore`'s already-anticipated
frameworks (it has NUnit/MSTest entries) unless there's a reason to differ.

## Local OPC UA / domain resources (available on this machine)

- **OPC UA Companion spec nodesets** are cloned locally at `C:\ode\UA-Nodeset`
  (`NodeSet2.xml` files) — read these for NodeId / namespace / type info instead
  of fetching from GitHub.

## Conventions

- This is an OPC UA client-side tool: be careful with encoding limits. OPC UA
  `MaxArrayLength` (Part 6 §5.2.2.15) applies to *every* decoded array,
  including service arrays like `nodesToRead`. Cap batch sizes by
  `min(OperationLimits.MaxNodesPerXxx, MaxArrayLength)` — not by the operation
  limit alone — or servers may return `BadEncodingLimitsExceeded`.

<!--
As the codebase grows, replace the placeholders above with the real solution
layout, actual build/lint/test commands (verified by running them), and any
architecture spanning multiple files. Remove anything that stops being true.
-->

