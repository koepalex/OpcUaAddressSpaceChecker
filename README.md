---
title: OpcUaAddressSpaceChecker
description: Validate OPC UA server address spaces against live or supplied NodeSet2 type models.
---

## Overview

OpcUaAddressSpaceChecker is a .NET global tool that connects to an OPC UA server,
browses its address space, and validates Object and Variable instances against
OPC UA type definitions, modelling rules, references, and selected companion
specification placement rules.

By default, the checker reads the type model live from the server's Types folder
(`i=86`). Supplying one or more `--nodeset` files is optional and uses those
NodeSet2 models as a type-model override for servers that need an explicit model.

## Install

Install the global tool package:

```powershell
dotnet tool install -g OpcUaAddressSpaceChecker
```

After installation, invoke it as:

```powershell
opcua-check-address-space --help
```

## Quick start

Validate a server with the live type model:

```powershell
opcua-check-address-space --endpoint opc.tcp://host:port
```

Write a SARIF report:

```powershell
opcua-check-address-space --endpoint opc.tcp://host:port --output-format sarif --output report.sarif
```

Use explicit NodeSet2 files as the type-model override:

```powershell
opcua-check-address-space --endpoint opc.tcp://host:port --nodeset .\MyModel.NodeSet2.xml --nodeset-dir .\nodesets
```

Validate only instances of a selected type or its subtypes:

```powershell
opcua-check-address-space --endpoint opc.tcp://host:port --type "nsu=http://opcfoundation.org/UA/Pumps/;i=1052"
```

## Options

| Option                         | Description                                                                                         | Default                                                  |
|--------------------------------|-----------------------------------------------------------------------------------------------------|----------------------------------------------------------|
| `--endpoint`, `-e`             | OPC UA server endpoint URL, for example `opc.tcp://localhost:4840`.                                  | `OPCUA_ENDPOINT`; otherwise required                     |
| `--security-mode`, `-m`        | Security mode: `None`, `Sign`, `SignAndEncrypt`.                                                    | `None`                                                   |
| `--security-policy`, `-p`      | Security policy: `None`, `Basic256Sha256`, `Aes128_Sha256_RsaOaep`, `Aes256_Sha256_RsaPss`.         | `None`                                                   |
| `--auth-mode`, `-a`            | Authentication mode: `Anonymous`, `UserName`, `Certificate`.                                        | `Anonymous`                                              |
| `--username`, `-u`             | Username for `UserName` authentication.                                                             | `OPCUA_USERNAME`; otherwise omitted                      |
| `--password`                   | Password for `UserName` authentication.                                                             | `OPCUA_PASSWORD`; otherwise omitted                      |
| `--password-from-stdin`        | Read the password from stdin.                                                                       | `false`                                                  |
| `--certificate-path`, `-c`     | Path to a client X.509 certificate in PFX format.                                                    | `OPCUA_CERTIFICATE_PATH`; otherwise omitted              |
| `--certificate-password`       | Password for the client certificate.                                                                | `OPCUA_CERTIFICATE_PASSWORD`; otherwise omitted          |
| `--certificate-from-stdin`     | Read a base64-encoded PFX certificate from stdin.                                                    | `false`                                                  |
| `--nodeset`                    | Optional NodeSet2 XML file to load as the type-model override instead of the live server types. May be specified multiple times. | Empty; live type model is used                           |
| `--nodeset-dir`                | Optional directory searched for companion NodeSet2 XML files when `--nodeset` is supplied. May be specified multiple times. | Empty                                                    |
| `--type`                       | Optional ObjectType or VariableType ExpandedNodeId. Validates only instances of that type or its subtypes. Prefer the namespace-URI form (`nsu=...`) because namespace indexes are server-specific. | Empty; all browsed nodes are validated                   |
| `--output-format`              | Output format: `console`, `json`, `sarif`, `markdown`.                                              | `console`                                                |
| `--output`, `-o`               | Optional output file path. Console output writes to stdout when omitted.                             | Omitted; writes to stdout                                |
| `--severity-threshold`         | Minimum severity included in results: `information`, `warning`, `error`.                            | `warning`                                                |
| `--view-completeness`          | Validation-view policy: `auto`, `complete`, or `restricted`. In `auto`, Anonymous is restricted and authenticated sessions are assumed complete unless access is denied. | `auto` |
| `--require-complete-view`      | Stop before validation unless the effective validation view is complete or assumed complete.         | `false`                                                  |
| `--strict-type-coverage`       | Promote `GEN-05` undeclared-child advisories from Information to Warning unless config overrides its severity. | `false`                                           |
| `--rule-id`                    | Rule ID to include. May be specified multiple times. Empty means all rules.                          | Empty; all rules included                                |
| `--exclude-rule`               | Rule ID to exclude. May be specified multiple times.                                                 | Empty; no rules excluded                                 |
| `--retry-count`                | Number of reconnection attempts on disconnect.                                                       | `3`                                                      |
| `--retry-delay`                | Delay between retries in seconds.                                                                   | `5`                                                      |
| `--verbose`, `-v`              | Enable verbose logging.                                                                             | `false`                                                  |
| `--log-file`                   | Optional path to a log file. Parent directories are created on demand.                               | `OPCUA_LOG_FILE`; otherwise omitted                      |

## Exit codes

| Exit code | Meaning                                                                                   |
|-----------|-------------------------------------------------------------------------------------------|
| `0`       | Validation completed with no Confirmed Warning/Error findings at or above the configured severity threshold. |
| `1`       | Confirmed validation findings met the configured severity threshold, no instances matched `--type`, stdin input was missing, or an unexpected runtime error occurred. |
| `2`       | The checker could not connect to the OPC UA server.                                        |
| `3`       | NodeSet2 type-model loading failed or the requested `--type` was unavailable in the selected type model. |
| `4`       | `--require-complete-view` was set but the effective validation view was restricted.        |
| `10`      | Command validation failed, such as a malformed `--type`, missing endpoint, invalid output format, invalid severity threshold, or missing authentication material. |
| `130`     | Operation was cancelled.                                                                  |

## Validation view, confidence, and exit behavior

OPC UA Browse is evaluated for the current Session and can omit nodes or References that the
current user is not allowed to Browse. A client cannot prove from visible data that no hidden child
exists. The checker therefore reports both severity and confidence:

* `Confirmed` means the finding is based on an observed mismatch, or absence is evaluated under a
  complete/assumed-complete policy.
* `Inconclusive` means absence may be caused by a restricted validation view. Inconclusive findings
  are included in reports but never fail the process.
* `Information` is advisory and never fails the process, including intentional instance extensions
  reported by `GEN-05`.

With `--view-completeness auto`, Anonymous sessions are treated as restricted. UserName and
Certificate sessions are reported as `AssumedComplete` for compatibility, but OPC UA does not prove
that assumption. Any observed `BadUserAccessDenied` downgrades automatic mode to restricted.
Use `--view-completeness complete` only when an operator can assert the credentials expose the
validation scope, or `restricted` to force conservative results.

## Rules catalog

| Id             | Category  | Severity | Description                                                                                      | Purpose                                                                                              | How to fix                                                                                             |
|----------------|-----------|----------|--------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------|
| `GEN-01`      | Generic   | Error    | Mandatory InstanceDeclarations must be present on instances at the declared BrowsePath.           | Ensures mandatory children defined by a type exist on each instance.                                 | Add the missing child at the declared BrowsePath with the expected BrowseName, NodeClass, and type.     |
| `GEN-02`      | Generic   | Error    | Instance children must use the same NodeClass as their InstanceDeclaration.                       | Prevents an Object, Variable, or Method from being exposed where the type declares another NodeClass. | Change the child NodeClass or use the correct declaration path for the intended node.                   |
| `GEN-03`      | Generic   | Error    | Instance child TypeDefinitions must match or subtype the declared TypeDefinition.                 | Verifies child instances preserve the type contract declared by their parent type.                    | Set the child's HasTypeDefinition to the declared type or a valid subtype.                              |
| `GEN-04`      | Generic   | Error    | Variable instances must match declared DataType, ValueRank, and ArrayDimensions constraints.      | Keeps Variable values compatible with the declared type model.                                       | Use a compatible DataType, ValueRank, and ArrayDimensions for the Variable.                             |
| `GEN-05`      | Generic   | Information | Reports instance-specific children that are not declared by the TypeDefinition.                 | Surfaces legal extensions for review without treating additional instance References as violations.  | No action is needed for an intentional extension. Define a subtype for reusable discovery contracts; investigate namespace collisions or misplaced standard children. |
| `GEN-06`      | Generic   | Error    | MandatoryPlaceholder declarations require at least one matching child.                            | Enforces placeholder declarations that require one or more concrete instances.                       | Add at least one child with a compatible ReferenceType and TypeDefinition below the placeholder parent.  |
| `GEN-07`      | Generic   | Warning  | Children assigned to OptionalPlaceholder declarations must match declared NodeClass, type, and reference constraints. | Assigns each child across all sibling placeholders before reporting one plausible mismatch.            | Use a compatible NodeClass, ReferenceType, and TypeDefinition for the intended placeholder.              |
| `GEN-08`      | Generic   | Warning  | Variable children must use reference types consistent with their declaration and variable kind.   | Detects Property/DataVariable reference mistakes such as HasComponent versus HasProperty.             | Link Properties with HasProperty, DataVariables with HasComponent, and match declared reference types.   |
| `GEN-09`      | Generic   | Error    | Observed nested InstanceDeclarations must appear at their declared structural BrowsePath, not directly below the instance root. | Detects existing children placed at the wrong level without duplicating missing-node findings.         | Move the observed node under the full declared BrowsePath.                                               |
| `GEN-10`      | Generic   | Error    | Object and Variable instances must expose a HasTypeDefinition.                                    | Ensures server-authored Object and Variable nodes can be validated; OPC UA core namespace nodes (`ns=0`) are skipped. | Add the missing HasTypeDefinition reference for non-core Object and Variable nodes.                      |
| `GEN-11`      | Generic   | Error    | Object and Variable instances must not directly instantiate abstract TypeDefinitions.             | Prevents concrete instances from using abstract types as their direct TypeDefinition.                 | Use a concrete subtype as the instance TypeDefinition.                                                   |
| `GEN-12`      | Generic   | Warning  | Present Method instances expose their declared InputArguments and OutputArguments Properties.       | Checks Method argument metadata without duplicating `GEN-01` when the Method itself is absent.         | Add the missing InputArguments or OutputArguments Property declared for the present Method.              |
| `GEN-13`      | Generic   | Warning  | Subtype InstanceDeclarations must not loosen ModellingRules inherited from supertypes.            | Detects type-model overrides that weaken inherited Mandatory or MandatoryPlaceholder constraints.     | Keep the inherited modelling rule or make the subtype stricter, not looser.                              |
| `GEN-14`      | Generic   | Warning  | Instance child BrowseName namespace indexes should match matching InstanceDeclarations.           | Helps catch children that have the right name but wrong namespace qualification.                      | Use the namespace index that corresponds to the declaration namespace URI.                               |
| `GEN-15`      | Generic   | Error    | Browsed targets must remain addressable when directly browsed or read.                              | Detects stale References whose target returns `BadNodeIdUnknown` instead of misclassifying lost metadata. | Fix or remove the stale Reference so its target NodeId can be browsed and read consistently.             |
| `ACCESS-01`   | Access    | Information | The current validation view may omit nodes or References.                                        | Explains why absence-based findings are Inconclusive for a restricted view.                            | Use credentials that expose the validation scope, or select the appropriate `--view-completeness` policy. |
| `DI-01`       | DI        | Error    | DeviceType instances expose all mandatory DI nameplate properties.                                | Enforces DI DeviceType nameplate completeness.                                                       | Add Manufacturer, Model, HardwareRevision, SoftwareRevision, DeviceRevision, DeviceManual, SerialNumber, and RevisionCounter as DI properties. |
| `DI-02`       | DI        | Warning  | DI ComponentType instances are reachable from the DI DeviceSet entry point.                       | Ensures DI components are discoverable from DeviceSet by hierarchical references.                     | Organize the root DI component under DeviceSet or make it reachable from DeviceSet.                     |
| `DI-03`       | DI        | Error    | DI ParameterSet and MethodSet optional structures have required contents when present.            | Validates optional TopologyElementType grouping objects once they are exposed.                        | Add at least one parameter Variable under ParameterSet and Method children under MethodSet when present. |
| `DI-04`       | DI        | Error    | Present DI Lock objects expose all mandatory lock state properties and methods.                   | Enforces the DI LockingServicesType structure when a Lock object is exposed.                         | Add Locked, LockingClient, LockingUser, RemainingLockTime, InitLock, RenewLock, ExitLock, and BreakLock. |
| `DI-05`       | DI        | Error    | DI DeviceType instances satisfy mandatory declarations inherited through HasInterface.            | Applies mandatory declarations from DI interfaces such as nameplate and health interfaces.            | Add the missing interface-derived declaration paths required by the DI DeviceType interfaces.            |
| `MACHINERY-01` | Machinery | Warning  | Machinery machine instances are reachable from the Machinery Machines entry point.                | Ensures Machinery instances are discoverable from the Machines folder.                               | Organize machine instances under the Machines entry point or make them hierarchically reachable from it. |
| `PUMPS-01`   | Pumps     | Warning  | Pumps Configuration object is typed as ConfigurationGroupType.                                    | Confirms PumpType Configuration uses the Pumps companion type expected by nested checks.              | Set the Configuration object TypeDefinition to ConfigurationGroupType or a valid subtype.                |
| `PUMPS-02`   | Pumps     | Error    | Pumps ConfigurationGroupType descendants are nested below Configuration.                          | Prevents ConfigurationGroupType descendants from being exposed directly below a pump.                 | Move ConfigurationGroupType descendant nodes under the pump Configuration object.                        |
| `PUMPS-03`   | Pumps     | Error    | Pumps Design and SystemRequirements objects are nested below Configuration.                       | Enforces the canonical PumpType path `Configuration/Design` and `Configuration/SystemRequirements`.  | Move Design and SystemRequirements below Configuration instead of directly below the pump.               |

## Output formats

The `--output-format` option selects how findings are rendered:

* `console` — a human-readable table on stdout (default).
* `json` — a machine-readable JSON document with validation-view metadata and per-finding confidence.
* `sarif` — SARIF 2.1.0 with validation-view run properties and per-result confidence.
* `markdown` — a Markdown report that groups findings per namespace.

### Markdown report

With `--output-format markdown` the tool emits a Markdown document that groups findings by the
namespace (companion specification) of each node's NodeId. Each namespace becomes a `##` section
whose heading links to the corresponding [OPC Foundation online reference](https://reference.opcfoundation.org),
followed by a GitHub-flavored Markdown table with one row per finding and the columns:

| Column | Contents |
|--------|----------|
| BrowsePath | The concrete absolute path to the node in the browsed address space (e.g. `13:FullMachineTool/5:Production/13:My Job1/5:ProductionPrograms`). Placeholder declaration segments are resolved to the actual fulfilling instances; angle brackets in any fallback text are HTML-encoded so they stay visible in a Markdown preview. |
| NodeId | The offending node's NodeId. |
| Rule violated | The rule ID, linked to its OPC Foundation reference. |
| Severity | `Information`, `Warning`, or `Error`. |
| Confidence | `Confirmed` or `Inconclusive`. |
| Short description | The finding message. |
| How to solve | Remediation guidance (mirrors the rules-catalog "How to fix" column). |
| Evidence | The finding details (actual-vs-expected), when available. |

The namespace grouping uses the live server's namespace table, so the report reflects the address
space that was actually validated. Write the report to a file with `-o`, for example:

```sh
dotnet run --project src\OpcUaAddressSpaceChecker -- --endpoint opc.tcp://localhost:4840 --output-format markdown -o report.md
```
