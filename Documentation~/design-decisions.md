[日本語](./design-decisions.ja.md)

# Design Decisions

Some AddressTeller behaviors are intentional choices that may seem inconvenient at first glance.
This document explains those decisions and the reasoning behind them. Before proposing a spec change, check whether the underlying assumptions described here have actually changed.

## Address Priority and Conflicts

`Order` is both the evaluation order and the address priority. If two or more rules that call `Address()` match the same asset, the one with the lowest `Order` wins and its address is written — this is the supported way to let a narrow rule override a broad one (see [Writing Rules](writing-rules.md#evaluation-rules-and-behavior) for an example). It is treated as a conflict — no write occurs — only when two or more of the *lowest-`Order`* matches tie. The same logic applies to both Apply and Validate.

Silently picking one of the tied rules would mean the final address depends on which of two equally-prioritized rules happens to be preferred, with no principled basis for the choice — the rules themselves declared the same priority, so there is nothing in their definitions to break the tie. Ambiguous states should surface as errors rather than being silently resolved.

This whole section is about a conflict *within one asset*: two or more rules disagreeing about what address that one asset should get. It says nothing about two *different* assets ending up with the same address string — that is a separate concern the per-asset conflict check above cannot see at all, since each asset is evaluated independently. `Validate` / `Apply All` (and therefore `CheckCLI` / `ApplyAllCLI` / `ApplyWithValidateCLI`) additionally scan the whole predicted result for duplicate addresses across assets and report it as `ValidationStatus.DuplicateAddress`. This is report-only — it never blocks a write, since Addressables' own Groups window accepts duplicate addresses too — but its severity still depends on whether AddressTeller can actually do anything about it: if at least one of the colliding assets is one AddressTeller itself would write an address for in that run, it is an error (something a rule can be adjusted to fix); if every colliding asset falls outside this run's address-writing scope (for example, two entries neither rule touches), it is a non-blocking notice instead.

## Labels Accumulate from All Rules

Unlike addresses, labels accumulate from all matching rules (multiple labels can be assigned simultaneously). Having multiple labels on a single asset is normal usage, and there is no concept of label conflict. To allow independent rules per classification axis, accumulation is the default.

## Missing Groups Are an Error (Default)

By default, if a rule specifies a group that does not exist, it is an error — groups are not created automatically. Groups are architectural units that carry bundle settings (compression and splitting policies), and silently creating groups from typos can go unnoticed. Group creation is left as an explicit operation. An opt-in setting (off by default) is available for cases where automatic creation is needed — **Auto-create missing groups** (`AutoCreateMissingGroups`) under `Project Settings > AddressTeller`; see [Project Settings](operations.md#project-settings) for details.

## Deletions Are Determined by Per-Asset Ownership

AddressTeller owns only the groups your rules declare an address in — a group becomes owned once at least one rule calls `Group("X")` and goes on to call `Address(...)` for it. Within an owned group, AddressTeller creates, moves, and deletes entries to match what the rules currently produce. AddressTeller never creates a new entry in any other group, and the cleanup described below never deletes or moves an entry out of one — but "not owned" doesn't mean "never touched": a label-only rule (`AnyGroup()`, or a `Group()` rule with no `Address()` yet) still adds labels to an existing entry there, since label writes are additive and not gated by ownership (see [Labels Accumulate from All Rules](#labels-accumulate-from-all-rules) above); and an asset matched by an address rule is moved into that rule's group from whatever group it currently sits in, even one AddressTeller does not own.

`CleanupStaleEntries` (default: ON) uses this ownership to decide what `Apply All` may remove. The determination is per-asset (GUID): an owned-group entry is removed once no rule matches the asset it belongs to anymore, meaning the asset generates neither an address nor a label (a truly unmatched asset). A label-only match (e.g. `AnyGroup()`, or a `Group()` rule with no `Address()`) still counts as a match, so the entry survives. As a consequence, a manually registered entry inside an owned group is removed once no rule matches it at all — if you want entries AddressTeller should leave alone, place them in a group it does not own.

Deletion is per-entry (`RemoveAssetEntry`), so both the address and any labels that entry held are lost together — there is no separate "strip labels but keep the entry" step. What is deliberately avoided is stripping labels from entries that remain matched: because labels accumulate from all rules by design, it is impossible to uniquely identify after the fact which rule assigned which label, so `Apply All` only ever adds labels to a surviving entry and never removes any label from a surviving entry. Automatic operations are limited to what ownership clearly covers (entries in owned groups); uncertain operations (retroactive label removal) are left to manual user action (e.g., Snapshot Exact restore).

Folders participate in this same ownership check like any other evaluated asset. A folder that no rule matches — including the case where no rule calls `IncludeFolders()` at all — is treated as unmatched, so with `CleanupStaleEntries` enabled its entry in an owned group is removed exactly like a file entry, whether AddressTeller or a user created it.

Some of the path exclusions described below (see [Folders Require Opting In](#folders-require-opting-in-includefolders)) have been tightened since earlier releases — for example, the Config Folder exclusion used to exclude only its actual contents, not folders that merely start with the same name. With `CleanupStaleEntries` enabled, the first apply after such a tightening can remove existing owned-group entries for assets that are newly considered invalid. See the "Upgrading from 0.4.x" note in [CHANGELOG.md](../CHANGELOG.md) for the specific categories this can affect and how to prepare before applying.

## Folders Require Opting In (IncludeFolders)

`AssetDatabase.GetAllAssetPaths()` returns folder paths alongside files. Rules that reason about paths by prefix — most notably `Match.InFolder(...)`, whose match is a simple `StartsWith` — would otherwise match subfolders as readily as the files inside them, and applying such a rule to a folder creates an Addressables *folder entry* that implicitly covers everything beneath it, double-managed against the per-asset entries AddressTeller already creates for the same files.

Rather than excluding folders outright (which would prevent rules from intentionally targeting folders, something Addressables itself allows when done manually), each rule opts in explicitly with `IncludeFolders()`. Rules that do not call it never see folders — `Where()` is not invoked for them — so existing rules keep behaving exactly as before.

Some folders are excluded even from rules that did opt in, because Addressables itself refuses to register them as an entry at all — the same path-validity check applies to folders as to files (see [Assets Excluded from Evaluation](writing-rules.md#assets-excluded-from-evaluation)), since it works on the path string alone without checking whether the path is actually a file or a folder. This excludes: the `Assets` root; a folder literally named `Editor`; a folder *underneath* one named `Editor` (this is a contents exclusion, not just the `Editor` folder itself — e.g. `Assets/Game/Editor/SubFolder` is excluded because `Assets/Game/Editor` is an ancestor); a package's own root folder (`Packages/<pkg>` with nothing beneath it); and the configured Addressables Config Folder itself and anything beneath it, matched by plain prefix like Addressables itself does (so a folder merely starting with the same name, e.g. `Assets/AddressableAssetsData_Backup`, is excluded too). Because the check only looks at the path string, it can also miss a real `Editor` folder nested directly under another folder whose name merely starts with `Editor` (e.g. `Assets/EditorArt/Editor` is not excluded) — this is a known limitation Addressables' own check has, and AddressTeller intentionally mirrors it rather than going further.

## AddressTeller Restricts Defaults, Never Removes the Choice — and Never Grants What Addressables Itself Refuses

AddressTeller's rules are compared against what Unity's own Addressables Groups window allows manually (drag-and-drop registration, the Addressable checkbox in the Inspector, etc.):

- If the Groups window lets you register something manually, AddressTeller's rules must leave a path to do the same — restricting it by default is fine, but the choice cannot be removed entirely. Folders are the concrete example: by default no rule sees them, but a rule can opt in with `IncludeFolders()` (see [Folders Require Opting In](#folders-require-opting-in-includefolders) above) to target something Addressables itself lets you register manually.
- Conversely, if the Groups window itself refuses to register something (for example, the `Editor` folder itself, the `Assets` root, special assets like `.preset`/`.asmdef`/a package's own `package.json`, anything outside `Assets/` or a package's own folder, or the configured Config Folder) — AddressTeller's rules must refuse it too (see [Assets Excluded from Evaluation](writing-rules.md#assets-excluded-from-evaluation)), rather than accidentally allowing it through automation.

This is compatible with defaulting to a conservative, safe behavior (see [Deletions Are Determined by Per-Asset Ownership](#deletions-are-determined-by-per-asset-ownership) above): restricting what happens by default is fine, as long as an explicit opt-in remains available for anything Addressables itself permits manually.

## Public API and Internal Implementation Boundary

To keep the surface area that users interact with as small as possible, only the following are `public`. Everything else — evaluation engine internals and Addressables integration details — is `internal`, leaving room for future refactoring without breaking changes.

Public API (representative examples; not an exhaustive list):

- **Rule-definition surface**: `AddressRuleBase`, `IAddressRuleBuilder`, `IAddressRuleGroupBuilder`, `Match`, `AssetCondition`, `Naming`, `AssetContext`, `AddressRuleEntry`
- **Execution entry points**: `AddressTellerService`, `AddressTellerSettings`
- **Snapshot**: `AddressTellerSnapshotService`, `SnapshotRestoreMode`, `SnapshotDiff`, `AddressTellerSnapshot`, `SnapshotEntry`
- **Result types**: `ValidationResult`, `ValidationStatus`
- **Progress reporting**: `IProgressReporter`, `NullProgressReporter`, `EditorProgressReporter`
- **Reports**: `AddressTellerReportWriter`, `ReportFormat`, `BundleDistributionSerializer`, `DistributionFormat`, and other report DTOs
- **CLI and menu entry points**: `AddressTellerMenu`, `AddressTellerSnapshotMenu`, `AddressTellerExplainMenu`, `AddressTellerCliArgs`
- **Auto-apply on import**: `AddressTellerPostprocessor`
- **Rule unit-testing support**: `RuleInspector` (from `AddressTeller.Testing` namespace) — allows inspection of rule configuration without a real Addressables project

Internal implementation (`internal`):

- Rule collection, evaluation, and explanation generation
- Addressables write implementation
- Snapshot file management, auto-evacuation, and report assembly

These are split across two editor assemblies, `AddressTeller.Core` and `AddressTeller.Editor` (see [Architecture](architecture.md) for the assembly layout). Tests can access them in a limited way via `InternalsVisibleTo` declared on each assembly. Because users are not expected to reference internal types directly, their signatures may change freely in future refactors.
