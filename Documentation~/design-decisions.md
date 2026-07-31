[日本語](./design-decisions.ja.md)

# Design Decisions

Some AddressTeller behaviors are intentional choices that may seem inconvenient at first glance.
This document explains those decisions and the reasoning behind them. Before proposing a spec change, check whether the underlying assumptions described here have actually changed.

## Address Conflicts Cause an Error

All rules are evaluated in ascending `Order`. If two or more rules that call `Address()` match the same asset, it is treated as a conflict — no write occurs. A single match is accepted. The same logic applies to both Apply and Validate.

Silently adopting the first match would mean the final address could change based on rule definition order or reflection enumeration order — subtle factors the user might not notice, leading to unintended addresses being adopted without warning. Ambiguous states should surface as errors rather than being silently resolved.

## Labels Accumulate from All Rules

Unlike addresses, labels accumulate from all matching rules (multiple labels can be assigned simultaneously). Having multiple labels on a single asset is normal usage, and there is no concept of label conflict. To allow independent rules per classification axis, accumulation is the default.

## Missing Groups Are an Error (Default)

By default, if a rule specifies a group that does not exist, it is an error — groups are not created automatically. Groups are architectural units that carry bundle settings (compression and splitting policies), and silently creating groups from typos can go unnoticed. Group creation is left as an explicit operation. An opt-in setting (off by default) is available for cases where automatic creation is needed.

## Deletions Are Determined by Per-Asset Ownership

`CleanupStaleEntries` (default: ON) removes assets from Addressables during `Apply All` when no rule matches them anymore, but only from groups managed by AddressTeller (groups referenced by at least one rule). Entries in groups that AddressTeller does not manage are never touched.

The determination is per-asset (GUID): only "does any rule currently match this asset?" is checked, meaning the asset generates neither an address nor a label (a truly unmatched asset). If a label-only rule (e.g. `AnyGroup()` or a `Group()` rule with no `Address()`) still matches, the asset is considered matched and its entry is not removed. Therefore, manually registered entries inside a managed group will be deleted if no rule matches them at all. If you want entries that AddressTeller rules do not target inside a managed group, place them in a separate group (one that AddressTeller does not manage).

The same per-asset ownership check gates label-only writes, not just deletions: when a label-only rule matches an asset that already has an entry, that entry's labels are only updated if the entry belongs to a group AddressTeller manages. Entries in unmanaged groups are neither deleted nor have labels added to them.

Deletion is per-entry (`RemoveAssetEntry`), so both the address and any labels that entry held are lost — there is no separate "strip labels but keep the entry" step. What is deliberately avoided is stripping labels from entries that remain matched: because labels accumulate from all rules by design, it is impossible to uniquely identify after the fact which rule assigned which label, so `Apply All` only ever adds labels to a surviving entry and never removes any label from a surviving entry. The scope of automatic operations is limited to what ownership clearly covers (entries in managed groups), leaving uncertain operations (retroactive label removal) to manual user action (e.g., Snapshot Exact restore).

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
