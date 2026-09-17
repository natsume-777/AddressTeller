[日本語](./compatibility.ja.md)

# Compatibility Policy

## Scope and Effective Date

While the version is `0.x`, breaking changes may land in a minor release. Each one is called out with **BREAKING** in [CHANGELOG.md](../CHANGELOG.md). From `1.0.0` onward, this document is the contract: breaking changes are limited to major releases, and are preceded by at least one release marking the affected API `[Obsolete]` (see [Deprecation Process](#deprecation-process-10)).

This document is enumerative, not descriptive: only what is explicitly listed below is covered by SemVer guarantees. Anything not listed — including behavior that happens to be stable today — can change in a minor or patch release without notice. See [What Is Not Covered](#what-is-not-covered).

SemVer is applied as follows once this policy takes effect (`1.0.0`+):

- **major** — a breaking change to anything listed under [What Is Covered](#what-is-covered)
- **minor** — backward-compatible additions (new types, members, enum values appended at the end, new CLI flags, new optional settings fields with safe defaults, etc.)
- **patch** — bug fixes that do not change any of the contracts below

## What Is Covered

### 1. Public C# API

Covered assemblies: `AddressTeller.Core` (namespace `AddressTeller`; the rule-inspection helper `AddressTeller.Testing.RuleInspector` also lives in this assembly) and `AddressTeller.Editor` (namespace `AddressTeller.Editor`). The public/protected surface of these two assemblies is the contract.

The source of truth is the approval test baseline: `Tests/Editor/PublicApiApproval/PublicAPI.AddressTeller.Core.approved.txt` and `PublicAPI.AddressTeller.Editor.approved.txt`. A change to these files that isn't a pure addition must be checked against the breaking/non-breaking lists below to classify it — the approval baseline is a mechanical detection signal, not the classification itself (for example, adding an optional parameter with a default value rewrites an existing line in the approved file, yet is listed as non-breaking below). The approval test (`PublicApiApprovalTests`) fails on any unapproved surface change, so the diff of these two files at release time is the starting point for that classification, not the verdict.

**Breaking** (major):
- Removing a public/protected type or member
- Renaming a public/protected type or member
- Changing a method/property signature (parameter types, parameter order, return type, adding a required parameter)
- Narrowing visibility (e.g. `public` → `internal`, removing a `protected` member from an inheritable class)
- Changing a type's namespace or the assembly it lives in
- Sealing a previously unsealed public class, or otherwise removing an extension point
- Changing the type of a public/protected field or event, including widening it to a less specific type

**Non-breaking** (minor):
- Adding a new public/protected type or member
- Adding an optional parameter with a default value to an existing method (does not change the signature seen by existing call sites)
- Widening a parameter type to a more general one without changing overload resolution for existing callers
- Adding a new method to a rule-builder interface (`IAddressRuleBuilder`, `IAddressRuleGroupBuilder`, `ILabelRuleBuilder`). These interfaces are only ever implemented internally (by `AddressRuleBuilderImpl`) and are meant to be consumed, not implemented, by rule authors — the usual breaking-interface-change concern (adding a member breaks every implementer) does not apply to them.

### 2. `-executeMethod` Entry Points

These are invoked by fully-qualified name from the command line, so the compiler cannot catch a rename — the break only surfaces when CI actually runs. They are covered independently of the general public-API rule above, and a rename is breaking even though it would already be caught by the public API approval test.

- `AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI`
- `AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI`
- `AddressTeller.Editor.AddressTellerMenu.CheckCLI`
- `AddressTeller.Editor.AddressTellerMenu.ClearCLI`

### 3. Command-Line Arguments

Parsed by `AddressTellerCliArgs.TryParse` (source of truth: `Editor/EntryPoints/AddressTellerCliArgs.cs`).

| Flag | Value(s) | Default if omitted |
|---|---|---|
| `-addressTellerReport <path>` | any file path | report not written |
| `-addressTellerReportFormat <format>` | `json`, `junit` | inferred from `-addressTellerReport`'s extension (`.xml` → `junit`, otherwise `json`); unset if no report path is given either |
| `-addressTellerDisableRules <names>` | comma-separated rule class full names | empty (no additional exclusions) |
| `-addressTellerConfirmClear` | presence-only flag (no value) | absent (treated as intentional refusal by `ClearCLI`) |
| `-addressTellerClearScope <scope>` | `all`, `managed` | `managed` |

Unrecognized arguments are silently ignored — this is itself part of the contract. A future flag can therefore never break a CI invocation that already happens to pass an argument the package doesn't yet recognize; conversely, this package must not start rejecting unknown arguments as an error in a later release.

**Non-breaking**: adding a new flag; adding a new accepted value to an existing flag's vocabulary (e.g. a third `-addressTellerClearScope` value).

**Breaking**: renaming a flag; removing a value from a flag's vocabulary; changing what a flag defaults to when omitted.

### 4. Exit Codes

From `Documentation~/operations.md`.

`ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI`:

| Exit code | Meaning |
|---|---|
| 0 | No drift, no issues |
| 1 | Drift detected (changes present, no Validation errors) |
| 2 | Validation errors present |
| 3 | Environment error (`AddressableAssetSettings` missing, invalid arguments, an unknown rule class name in `-addressTellerDisableRules`, or report write failure) |

`ClearCLI`:

| Exit code | Meaning |
|---|---|
| 0 | Clear completed |
| 3 | Environment error (`AddressableAssetSettings` missing, invalid arguments, a rule configuration error that makes `managedGroups` untrustworthy for `scope=managed`, or snapshot save failure) |
| 4 | Rejected because `-addressTellerConfirmClear` was not specified |

Adding a **new** exit code value (for either CLI family) is treated as a **major** change, not minor, even though CI scripts that only check specific known codes wouldn't necessarily break. This is because CI scripts commonly branch with an equality check per known code and treat "anything else" as an unexpected failure category (e.g. `case 0/1/2/3: ... ; default: fail the build`); introducing a new code changes what "anything else" catches even if no existing branch's meaning changes.

### 5. Report Output (JSON / JUnit XML)

Produced by the internal `AddressTellerReportBuilder` (named here only to point at where the logic lives — it is not itself part of the public API; see [What Is Not Covered](#what-is-not-covered)) and written to disk by the public `AddressTellerReportWriter`, from the public `AddressTellerReport` type (`Editor/Application/Reporting/AddressTellerReport.cs`). Serialized with `JsonUtility`, which uses field names verbatim (no camelCase conversion) — the JSON keys below are exactly the C# field names.

**JSON keys** (nesting shown by indentation):

```
Summary
  Added, Removed, Changed, Issues, ExitCode         (all System.Int32)
Drift[]
  Guid, Path, ChangeType                            (System.String)
  Before / After
    Address, GroupName                              (System.String)
    Labels[]                                        (System.String)
Issues[]
  Path, Status, Message                             (System.String)
BundleDistribution                                  (always present; see note below)
  Bundles[]
    GroupName, Mode, SplitKey                        (System.String)
    AssetCount                                        (System.Int32)
  TotalLogicalBundleCount, UnknownGroupCount          (System.Int32)
  Disclaimer                                          (System.String)
SchemaVersion                                         (System.Int32)
```

**`BundleDistribution`**: this key is always present in the JSON output, never omitted and never JSON `null` — `JsonUtility` has no way to represent a null reference for a non-`UnityEngine.Object` `[Serializable]` class, so a null `BundleDistribution` is serialized as an object with every field at its C# default (`Bundles: []`, `TotalLogicalBundleCount: 0`, `UnknownGroupCount: 0`, `Disclaimer: ""`) instead of being omitted or written as `null`. This all-defaults shape appears when the report is built from a dry-run whose `After` is null, when no `AddressableAssetSettings` was supplied, or when the bundle-distribution calculation itself throws (caught internally and logged as a warning). Consumers should treat that combination — all-zero counts, an empty `Bundles[]`, and an empty `Disclaimer` — as "not calculated," not as "zero bundles produced."

**Value vocabulary**:

- `ChangeType`: fixed strings `"Added"`, `"Removed"`, `"Changed"`.
- `Status`: the name of a `ValidationStatus` member (e.g. `"ConflictingAddress"`). See [Enums](#enums) for how this list can grow.
- `Mode` (inside `Bundles[]`): the name of a `BundleModeKind` member (`PackTogether`, `PackSeparately`, `PackTogetherByLabel`, `Unknown`).
- `SplitKey` (inside `Bundles[]`): for `PackTogether`, the fixed string `"all"`; for `Unknown`, the fixed string `"(unknown)"` (`BundleDistributionCalculator.UnknownSplitKey`); for `PackTogetherByLabel`, either the fixed string `"(no labels)"` (`BundleDistributionCalculator.NoLabelsSplitKey`) when the bundle's assets have no labels, or the asset's labels sorted (Ordinal) and joined with `|`; for `PackSeparately`, the asset identifier (not part of this fixed vocabulary).
- `Bundles[]` order: sorted by `GroupName` (Ordinal), then by `Mode`'s underlying numeric value (not a string comparison), then by `SplitKey` (Ordinal). Assigning a new `BundleModeKind` member a numeric value that falls between two existing members' values changes this ordering — see [Enums](#enums).

**`SchemaVersion`**: currently `1` (`AddressTellerReport.CurrentSchemaVersion`). Incremented whenever a shape change above would otherwise be silently backward-incompatible for a consumer parsing the JSON structurally (as opposed to just reading new optional fields). `AddressTellerReport.FromJson` rejects a `SchemaVersion` greater than the currently-supported value, returning `null` and logging a warning that includes only the offending version numbers, not the source file path. Only this rejection policy mirrors `AddressTellerSnapshotService.LoadFromFile`'s handling of snapshot files (see [Snapshot Files](#6-snapshot-files)) — unlike `LoadFromFile`, `FromJson` performs no other content validation and does not catch exceptions from the underlying `JsonUtility` call, so malformed JSON propagates as whatever exception `JsonUtility` throws, rather than being reported through a return value. A JSON payload that omits the `SchemaVersion` key entirely (e.g. a hand-edited file, or a report produced before this field existed) is read back with `SchemaVersion` equal to `AddressTellerReport.CurrentSchemaVersion`: `JsonUtility.FromJson` populates a freshly constructed instance and only overwrites fields present in the JSON, so a missing key leaves the field's initializer value in place rather than the type's zero-value default. There is currently no CLI path that calls `FromJson` to re-read a report file, but external tooling that parses these reports directly should apply the same "treat higher as unsupported" rule and should not rely on a missing key meaning "unknown version."

**JUnit XML** (`AddressTellerReportWriter.ToJUnitXml`):

- `<testsuite name="AddressTeller" tests="..." failures="...">` — the `name` attribute is frozen at `"AddressTeller"`.
- One `<testcase>` for drift as a whole: `name="drift"`, `classname="AddressTeller.Drift"`.
- One `<testcase>` per distinct `ValidationStatus` name present among the report's issues: `name="<StatusName>"` (e.g. `"ConflictingAddress"`), `classname="AddressTeller.Validation"`.
- `tests` / `failures` counts and the presence of a nested `<failure>` element follow standard JUnit consumer expectations (a `<testcase>` without `<failure>` passed; with `<failure>` it failed).

**Consumer obligations**: ignore unknown JSON fields (do not fail on additions); check `SchemaVersion` against the highest version you were built to understand and treat higher as unsupported rather than guessing at the shape.

**Explicitly not covered**: the `Disclaimer` string's exact wording, and the JUnit `<failure>` element's `message` attribute / body text. Both are free-form and may be reworded at any time; do not assert on their content, only on their presence/absence. That said, `Disclaimer` is guaranteed non-empty whenever `BundleDistribution` was actually calculated (`AddressTellerReportBuilder` always assigns it a constant, non-empty string in that path) — an empty `Disclaimer` is one of the signals that the section falls into the "not calculated" all-defaults shape described above, even though the non-empty text itself carries no wording guarantee.

### 6. Snapshot Files

This is the JSON produced by serializing (`JsonUtility`) the `AddressTellerSnapshot` that `AddressTellerSnapshotService.Capture` assembles; the actual file write happens in `AddressTellerAutoSnapshotService`, `AddressTellerClearSnapshotService`, or the Save Snapshot menu item, not in `Capture` itself. Read back by `AddressTellerSnapshotService.LoadFromFile` (`Editor/Application/Snapshot/AddressTellerSnapshotService.cs`). Also `JsonUtility`-serialized, so the same verbatim-field-name rule applies.

**JSON keys**:

```
Entries[]
  Guid, Address, GroupName                (System.String)
  Labels[]                                (System.String)
CapturedAtIso, Comment, UnityVersion, PackageVersion   (System.String)
SchemaVersion                             (System.Int32)
```

**`SchemaVersion`**: currently `1` (`AddressTellerSnapshotService.CurrentSchemaVersion`). Both the snapshot and report formats reject a `SchemaVersion` greater than the currently-supported value (see [Report Output](#5-report-output-json--junit-xml)). The two differ in: (a) the treatment of `0` — for snapshots, `0` means "older-format JSON that predates this field, or an uninitialized instance" and is accepted (`AddressTellerSnapshot.SchemaVersion`'s field initializer is itself `0`, the same as the CLR default, so a snapshot JSON missing the key also reads back as `0`); the report format has no equivalent documented meaning for `0`, since `AddressTellerReport.SchemaVersion`'s field initializer is `AddressTellerReport.CurrentSchemaVersion` (currently `1`, not `0` — see [Report Output](#5-report-output-json--junit-xml) for what a report JSON missing the key reads back as); and (b) where the check is performed — for snapshots it is the service layer (`LoadFromFile`), which also validates the rest of the file's content (e.g. rejecting duplicate/empty GUIDs) and reports failures via `out error` rather than logging; for reports the check lives on the DTO type itself (`AddressTellerReport.FromJson`), which performs no other content validation.

**Folder layout**: snapshots are written under `AddressTellerSettings.SnapshotFolder` (configurable; default `AddressTellerSnapshots/` at the project root). Two reserved subfolders carry contractual meaning:
- `SnapshotFolder/Auto/` — automatic safety snapshots taken before `Apply All` / `Apply with Validate` from the menu, consumed by `Tools/AddressTeller/Undo Last Apply`. Subject to rotation (`AddressTellerSettings.AutoSnapshotRetention`; see `AddressTellerAutoSnapshotService`), so old files in this subfolder are deleted automatically once the retention count is exceeded.
- `SnapshotFolder/Clear/` — the dedicated snapshot taken before `Tools/AddressTeller/Clear All Addresses & Labels...` / `ClearCLI`. Not subject to the Auto subfolder's rotation, and has no rotation of its own (`AddressTellerClearSnapshotService`) — files here are never deleted automatically.

Tooling that locates the latest auto-snapshot or clear-snapshot by scanning these subfolders can rely on their names, and on the Clear subfolder never being pruned by the Auto rotation.

### 7. Settings Asset

Persisted at `ProjectSettings/AddressTellerSettings.asset` (a `ScriptableSingleton`, `Editor/Application/AddressTellerSettings.cs`), intended to be checked into version control and shared across a team.

**Serialized field names** (all `[SerializeField] internal`, on `AddressTellerSettingsAsset`):

| Field | Type | Default |
|---|---|---|
| `_cleanupStaleEntries` | `bool` | `true` |
| `_postprocessEnabled` | `bool` | `true` |
| `_snapshotFolder` | `string` | `"AddressTellerSnapshots"` |
| `_autoSnapshotBeforeApplyAll` | `bool` | `true` |
| `_autoSnapshotRetention` | `int` | `10` |
| `_autoCreateMissingGroups` | `bool` | `false` |
| `_postprocessOrder` | `int` | `1000` (see below) |
| `_disabledRuleClassNames` | `List<string>` | empty |

Renaming any of these fields without a `[FormerlySerializedAs]` pointing at the old name is prohibited — it would silently reset that setting to its default for every project that already has a committed `AddressTellerSettings.asset`, with no error or warning. Changing a field's default value is a **major** (breaking) change, since it changes behavior for projects that never explicitly set the field.

`_postprocessOrder`'s `0` is reserved as an "unset" sentinel: both an existing asset with the field left at its zero-value default from before this setting existed, and a project that explicitly sets it to `0`, read back as `AddressTellerSettings.DefaultPostprocessOrder` (`1000`). This is a deliberate consequence of the field-rename rule above — the package cannot distinguish "never set" from "explicitly set to 0" in a Unity-serialized `int`, so both are folded into the same fallback.

The Project Settings UI itself (`Project Settings > AddressTeller`, registered at provider path `Project/AddressTeller`) is **not** covered — its layout, field ordering, and descriptive text may change freely.

### 8. Menu Paths

All `[MenuItem]` paths, from `Editor/EntryPoints/`:

- `Tools/AddressTeller/Apply All`
- `Tools/AddressTeller/Preview Group...`
- `Tools/AddressTeller/Clear All Addresses & Labels...`
- `Tools/AddressTeller/Validate`
- `Tools/AddressTeller/Apply with Validate`
- `Tools/AddressTeller/Snapshot/Save Snapshot`
- `Tools/AddressTeller/Snapshot/Manage Snapshots...`
- `Tools/AddressTeller/Undo Last Apply`
- `Assets/AddressTeller/Explain`
- `Assets/AddressTeller/Preview (Apply Preview)`

Renaming or removing any of these paths is breaking (documentation, muscle memory, and any recorded macros reference them by string). Adding a new menu item is non-breaking.

### 9. Rule Authoring Behavior

The following aspects of how `AddressRuleBase` subclasses are collected and evaluated are part of the contract, since a user's rule classes are written against this behavior:

- **Collection**: rule classes are found via reflection across all loaded assemblies (excluding assemblies that reference `nunit.framework`), requiring a non-abstract type with a public parameterless constructor. Open generic types are not filtered out separately — they pass the constructor check but `Activator.CreateInstance` throws for them at instantiation time, so they end up handled the same as a rule class whose constructor throws: skipped and logged as a warning rather than aborting collection.
- **Order**: rules are evaluated in ascending `Order`; ties are broken deterministically by the rule class's full type name (Ordinal). Duplicate `Order` values across classes produce a warning but are not an error.
- **Conflicts**: if two or more matching rules call `Address()` for the same asset, this is a conflict (`ValidationStatus.ConflictingAddress`) and neither the address nor any labels are written for that asset — the whole write for that asset is skipped, not just the address.
- **Label accumulation**: `Label()` calls from every matching rule accumulate on an asset; labels are never implicitly removed by a rule that stops matching (see `CleanupStaleEntries` for the one path that does remove labels, by deleting the whole entry).
- **`Where()` / `Address()` / `IncludeFolders()` single-call constraint**: calling any of these a second time on the same rule chain throws `InvalidOperationException`.
- **`GroupDefault()` resolution**: resolved from `AddressableAssetSettings.DefaultGroup` at evaluation time (not baked in at `Configure()` time), so it follows DefaultGroup renames automatically.
- **Folders**: a folder asset never reaches a rule's `Where()` (the predicate is not even invoked) unless that rule opts in with `IncludeFolders()`. This keeps existing rules, written with files in mind, from unintentionally matching a folder through a broad `Where` condition.
- **Stale entry cleanup scope**: when `CleanupStaleEntries` is enabled, cleanup also removes entries in managed groups whose asset path is structurally invalid for an Addressables entry (not just entries no longer matched by any rule) — for example leftovers created by an older AddressTeller version, identified by extension, an `Editor`-named folder, or similar. Entries whose path cannot currently be resolved at all (`AddressableAssetEntry.AssetPath` is empty — e.g. an asset temporarily unavailable due to an unfetched LFS pointer, an in-progress branch switch, or a missing package) are excluded from this check; a genuinely deleted asset is instead handled by the separate deletion-notification path (`RemoveEntriesForDeletedAssets`). This check runs on every managed entry currently in Addressables, independent of which paths were passed to Apply (and its dry-run/Preview).

Adding a new, off-by-default opt-in setting that changes evaluation behavior only when explicitly enabled is non-breaking, since it does not change behavior for projects that don't opt in.

## Enums

`ValidationStatus`, `ClearScope`, `ReportFormat`, `DistributionFormat`, `SnapshotRestoreMode`, and `BundleModeKind` are all **open enums**: this package may append new members to any of them in a minor release. Renaming a member, removing a member, or changing an existing member's assigned numeric value (including reusing an already-assigned number for a different member) is a **major** (breaking) change. From `1.0.0`, each of these enums' member-to-number mapping is frozen; new members are only ever appended after the highest existing value. Reordering members' declarations in source without changing any member's assigned value is neither breaking nor detectable — see [How This Is Enforced](#how-this-is-enforced). Even before `1.0.0`, an accidental rename, removal, or number shift is already caught mechanically — see the same section.

**Why appending a member doesn't break compilation but can break behavior at runtime**: a `switch` statement without a `default` arm compiles and runs fine against a new enum value it doesn't recognize — it just silently does nothing (or falls through, depending on the surrounding code), which is usually the wrong behavior for a status the caller has never seen. This is why appending is minor rather than patch: it is meant to be visible in a changelog and considered by anyone who switches exhaustively over these types, even though it cannot fail a build.

Concretely, `ValidationStatus` currently has: `Ok`, `Skipped`, `LabelsOnly`, `ConflictingAddress`, `GroupNotFound`, `InvalidAddress`, `RuleError`, `GroupWillBeCreated`, `GroupCreationFailed`, `DefaultGroupUnavailable`, `RuleConfigureFailed`, `EntryRejectedByAddressables`. This is the enum most likely to keep growing (it is the package's general-purpose "what happened for this asset" result type), so a `switch` over it is the most important place to have a `default` arm.

`[Flags]` is deliberately not used for any of these enums, even though some (`ValidationStatus` in particular) might look combinable. A `ValidationResult` represents exactly one outcome for one asset; using `[Flags]` would imply combinations are meaningful and would also change the JSON/enum-name serialization story (a `[Flags]` `ToString()` can produce comma-joined names for combined values), which is a larger compatibility surface this package does not want to commit to.

**Consumer obligations**:
- Place a `default` arm in every `switch` over one of these enums, and treat the default case as "unknown, handle conservatively" rather than silently ignoring it.
- Persist these values by member **name** (e.g. in a snapshot, log, or external config), never by underlying number — the number is only guaranteed stable from `1.0.0`, and even then a name-based persistence format survives a hypothetical future all-new enum better than a number-based one would.
- When you encounter an enum value your code doesn't recognize, prefer failing closed (treat it as a problem needing attention) rather than silently treating it as OK.

**Enum numeric value exposed in report ordering**: `BundleModeKind`'s underlying numeric value is used as a sort tie-break for the `Bundles[]` array in both the JSON report (see [Report Output](#5-report-output-json--junit-xml)) and `LogicalBundle`'s in-memory ordering (`BundleDistributionCalculator.Calculate`). Appending a new `BundleModeKind` member with a value greater than every existing member's does not change this ordering for existing modes; assigning it a value that falls between two existing members' values would.

## What Is Not Covered

- `internal` types and members, including ones made visible to test/sample assemblies via `InternalsVisibleTo` — except where explicitly listed above (e.g. the settings asset's serialized field names in [Settings Asset](#7-settings-asset)).
- Log message text, dialog text, window titles/layout, and USS/UI styling.
- The order of any collection this document doesn't explicitly say is ordered (most public APIs that return collections do document their ordering in XML docs; where they don't, no order is guaranteed).
- The exact source contents of the sample packages under `Samples~/` (they may be edited for clarity; the APIs they demonstrate are still covered).
- Performance characteristics (timing, allocations).
- The minimum supported Unity / Addressables version. Raising either minimum is done in a **minor** release (not major), since it does not change this package's own API but does require action from users on an older Editor/Addressables version. Any such change is called out explicitly in CHANGELOG.md.

## Deprecation Process (1.0+)

From `1.0.0`, removing or breaking anything in [What Is Covered](#what-is-covered) requires:

1. At least one prior minor release that marks the affected member `[Obsolete]` (with a message pointing at the replacement, where one exists).
2. The actual removal/break lands in the next major release, not sooner.

Before `1.0.0`, this process is not guaranteed — a **BREAKING** entry in CHANGELOG.md is the only notice given, per the [Scope and Effective Date](#scope-and-effective-date) section above.

## How This Is Enforced

- For the [Public C# API](#1-public-c-api), the approval test baseline files are the mechanical check: any unapproved diff fails `PublicApiApprovalTests`. A change to these `.approved.txt` files must be paired with a CHANGELOG.md entry (marked **BREAKING** if applicable) in the same change. This extends to the enum member-to-number mapping described in [Enums](#enums): the baseline records each member as `EnumMember <name> = <value>` (`PublicApiSurfaceFormatter.FormatType`), so a rename, a removal, or a change to a member's assigned numeric value changes that member's recorded line and is caught the same way as any other public API change — the classification (breaking vs. not) still follows the rules in [Enums](#enums), the baseline diff is only the detection signal. Because every member of all six of these enums carries an explicit numeric value (none rely on the compiler's auto-increment), what this does *not* catch is reordering an enum's declaration in source without changing any member's assigned value — since the approved-file text is sorted by member name rather than declaration order. This has no runtime effect either, since sorting/comparison over these enums (e.g. the `Bundles[]` tie-break in [Report Output](#5-report-output-json--junit-xml)) uses the numeric value, not the member's position in the source file.
- Everything else in this document (CLI entry points, arguments, exit codes, report/snapshot formats, settings field names, menu paths, rule-authoring behavior) has no automated detection — nothing will fail CI if one of these silently changes. This document's enumeration is the only thing standing between an incidental change and a broken consumer; when changing any of the source files referenced above, check whether the change touches something listed here.
