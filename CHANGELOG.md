[日本語](./CHANGELOG.ja.md)

# Changelog

This file follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
Versioning follows [Semantic Versioning](https://semver.org/).
While the version is `0.x`, breaking changes may land in a minor release; each one is marked **BREAKING** below. From `1.0.0` onward the guarantees in [Compatibility Policy](Documentation~/compatibility.md) apply: breaking changes are limited to major releases and are preceded by at least one release marking the affected API `[Obsolete]`.

## [Unreleased]

---

## [0.6.1] - 2026-09-23

### Fixed

- JUnit report: `CheckCLI` now attaches a `<failure>` to a Validation `<testcase>` only when its status has
  at least one `IsOk=false` result — matching `CheckCLI`'s own exit code 2 criteria, so a `DuplicateAddress`
  notice that only involves duplicates outside this run's writes (`IsOk=true`) does not get a `<failure>`.
  `ApplyAllCLI` and `ApplyWithValidateCLI` now attach `<failure>` only to a status that actually blocks
  Apply, matching their own exit code 2 criteria: a `DuplicateAddress` that does not block the write no
  longer produces a `<failure>` from these two, even when reported as `IsOk=false` — this matches 0.6.0's
  documented exit code contract, which already excluded `DuplicateAddress` from aborting Apply or affecting
  its exit code. `<testcase>` elements are still generated for every status present, as before (unchanged);
  only the presence of the nested `<failure>` element changes. Direct calls to the public
  `AddressTellerReportWriter.ToJUnitXml` / `WriteToFile` overloads are unchanged: they still attach
  `<failure>` unconditionally to every Validation `<testcase>`, regardless of `IsOk`.
- Snapshot auto-rotation: if `Auto-snapshot retention count` is manually edited in
  `ProjectSettings/AddressTellerSettings.json` to 0 or less, the safety snapshot `CaptureAndSave` had just
  saved before an apply runs could be deleted by the rotation that same call performs right after saving it
  — before Apply itself has even started — leaving `Undo Last Apply` unable to restore it. The count is now
  clamped to 1 on load (in-memory only; the JSON file is not re-written with the correction), and a
  `Warning` is logged. Values set via Project Settings UI are already constrained to 1 or more.

### Changed

- Project Settings UI: the empty-list message for "Managed Groups" now reflects the current ownership
  definition: groups targeted by enabled rules' `Address()` calls, not just `Group()` references. Added a
  note to `IAddressRuleBuilder.Group()`'s XML doc that Addressables replaces `/` and `\` in a group name
  with `-` when the group is actually created or renamed, so a `Group()` name containing either character
  may not match the group Addressables ends up with; changed the Writing Rules example accordingly
  (`BossAudio` instead of `Audio/Boss`).

### Documentation

- Documented that `Summary.Issues` counts every entry in `Issues[]`, including report-only notices that
  don't affect `Summary.ExitCode` — check `Summary.ExitCode` to tell whether a report represents a passing
  or failing run, not this count. The value itself is unchanged.
- Documented that the JUnit report is built from the pre-Apply dry-run result, not the actual execution
  result an apply's exit code is based on, so a status that can only be produced by actually performing a
  write (e.g. `ValidationStatus.EntryRejectedByAddressables`) can affect `ApplyAllCLI` / `ApplyWithValidateCLI`'s
  exit code without ever appearing in that run's JUnit output.
- Expanded the 0.6.0 upgrade guidance for the settings-storage change into a dedicated "Upgrading from
  0.5.x" note: a fuller list of the settings that reset to their defaults, what an early automatic apply
  on import can and cannot do before you get a chance to restore your values, and safeguards that don't
  depend on that timing. Also fixed inaccurate/incomplete notes for `ValidationResult.IsOk`, the
  label-only rule change, and `PostprocessOrder` (all BREAKING), and for the `TypeBasedRules` sample
  (not BREAKING).

---

## [0.6.0] - 2026-09-22

_Several entries below were corrected after this version's initial release — see [Unreleased](#unreleased)
above for what changed. The corrections only affect this document; 0.6.0's actual behavior is unchanged._

### Added

- Broad-rule-overridden-by-specific-rule address authoring: `AddressRuleBase.Order` now also acts as a
  priority when two or more matching rules produce an address for the same asset. See
  [Writing Rules: Address priority and conflicts](Documentation~/writing-rules.md#evaluation-rules-and-behavior)
  for an example.
- Cross-asset duplicate address detection: `Validate` / `Apply All` (and `CheckCLI` / `ApplyAllCLI` /
  `ApplyWithValidateCLI`) now report when two different assets resolve to the same address
  (`ValidationStatus.DuplicateAddress`), as an error or a non-blocking notice depending on whether
  AddressTeller itself would write one of the colliding addresses. See
  [Design Decisions: Address Priority and Conflicts](Documentation~/design-decisions.md#address-priority-and-conflicts).

### Changed

- **BREAKING**: `AddressRuleBase.Order` now doubles as an address priority, not just an evaluation order.
  When two or more matching rules produce an address for the same asset, the one with the lowest `Order`
  wins instead of it always being a conflict; it is only a conflict when the lowest-`Order` matches tie.
  If your project has assets that were previously reported as `ConflictingAddress` (and therefore never
  written), run Preview (dry-run) before the first `Apply All` after upgrading — those assets will now be
  written using whichever matching rule has the lowest `Order`, including via auto-apply on import if that
  setting is enabled. See
  [Design Decisions: Address Priority and Conflicts](Documentation~/design-decisions.md#address-priority-and-conflicts)
  and [Compatibility: Rule Authoring Behavior](Documentation~/compatibility.md#9-rule-authoring-behavior).
- **BREAKING**: Settings now persist to `ProjectSettings/AddressTellerSettings.json` instead of
  `ProjectSettings/AddressTellerSettings.asset`. The old `.asset` file is no longer read, and there is no
  migration from it: the first time this version runs against a project that does not yet have that
  `.json` file, every setting resets to its default — **Auto-apply on import** and
  **Remove unmatched entries** turn back ON if you had turned either off; every rule class you had
  disabled in the rule list becomes enabled again; **Snapshot folder** reverts to
  `AddressTellerSnapshots`, which can make `Tools/AddressTeller/Undo Last Apply` and
  `Tools/AddressTeller/Snapshot/Manage Snapshots...` unable to find snapshots saved under a different
  folder until you set it back; and **Auto-create missing groups** turns back OFF, which can turn a
  previously-working `Apply All` into a `GroupNotFound` error for a rule that relied on it. See
  **Upgrading from 0.5.x** below for what this reset can trigger and how to prepare for it. See
  [Settings Asset](Documentation~/compatibility.md#7-settings-asset).
- **BREAKING**: Ownership for every deletion-related operation — `CleanupStaleEntries`, the invalid-path
  entry sweep, deletion follow-up for deleted assets, `ClearScope.Managed` (including CLI
  `-addressTellerClearScope managed`), and the removable-entry filter in `Undo Last Apply` — is now
  determined by which groups a rule declares `Address()` for, not by every group referenced via
  `Group()`. Each of these now considers a narrower set of groups "managed": a `Group("X")` rule that
  hasn't called `Address()` yet no longer makes group `X` eligible. See
  [Design Decisions: Deletions Are Determined by Per-Asset Ownership](Documentation~/design-decisions.md#deletions-are-determined-by-per-asset-ownership).
- **BREAKING**: Label-only rules (`AnyGroup()`, or a `Group()` rule with no `Address()`) now add labels
  to an asset's existing entry regardless of which group it belongs to — previously, labels were only
  added when that group was referenced by some *enabled* rule's `Group()` call (a rule disabled in
  Project Settings did not count, and neither did a `GroupDefault()` reference left unresolved because
  `AddressableAssetSettings.DefaultGroup` was unavailable), including the label-only rule's own reference,
  if it made one and was itself enabled: an enabled `Group("X")` rule with no `Address()` already reached
  entries sitting in group `X` by virtue of that call alone. What it could not previously reach was an
  entry in a group no enabled rule referenced via `Group()` at all, or, for `AnyGroup()`, any entry
  outside that referenced set. If your project has such a rule, run
  Preview (dry-run) before the first `Apply All` after upgrading: entries in groups the rule previously
  couldn't reach will gain labels, including via auto-apply on import if that setting is enabled. See
  [Writing Rules: AnyGroup](Documentation~/writing-rules.md#anygroup).
- **BREAKING**: `ApplyAllCLI` / `ApplyWithValidateCLI` no longer exit with code 1 for a completed apply
  that had drift; a successful apply now always exits 0. See
  [Compatibility: Exit Codes](Documentation~/compatibility.md#4-exit-codes).
- **BREAKING**: `ApplyAllCLI` / `ApplyWithValidateCLI`'s JUnit report no longer reports drift as a
  `<failure>` on the `drift` testcase, matching the exit code change above (a completed apply's own
  report should not fail a CI job that ingests it). `AddressTellerReportWriter.ToJUnitXml` /
  `WriteToFile` gained an optional `treatDriftAsFailure` parameter for this (default `true`, matching
  `CheckCLI`'s existing behavior). See
  [Compatibility: Report Output](Documentation~/compatibility.md#5-report-output-json--junit-xml).
- **BREAKING**: `AddressTellerCliArgs.TryParse` now rejects an unrecognized `-addressTeller`-prefixed
  argument as a parse error instead of silently ignoring it. See
  [Compatibility: Command-Line Arguments](Documentation~/compatibility.md#3-command-line-arguments).
- `ApplyAllCLI` / `ApplyWithValidateCLI` / `ClearCLI` now call `AssetDatabase.SaveAssets()` right before
  exiting. See [Apply & Operations: CI Integration](Documentation~/operations.md#ci-integration).
- Projects with a pre-existing duplicate address may now see `CheckCLI` exit 2 where it previously
  exited 0 or 1. `ApplyAllCLI` / `ApplyWithValidateCLI` / `Apply with Validate` are unaffected by a
  duplicate either way — it is only ever logged and reported, never a reason to abort or to change
  their exit code. See
  [Design Decisions: Address Priority and Conflicts](Documentation~/design-decisions.md#address-priority-and-conflicts).
- **BREAKING**: `!ValidationResult.IsOk` no longer always means the asset's write was skipped this run.
  Two statuses can have `IsOk=false` while a write still happens: `ValidationStatus.RuleError` (tied to
  the asset via `Context`, returned by both `ApplyAll` and `ValidateAll`) — if the exception came from
  the highest-priority (lowest-`Order`) matching rule, that rule's candidate is simply absent from
  evaluation and a lower-priority rule's address is still written for the asset when one exists; this was
  already true before this release, see
  [Writing Rules: Exception inside a rule](Documentation~/writing-rules.md#evaluation-rules-and-behavior)
  — and `ValidationStatus.DuplicateAddress` (not tied to a single asset — `Context` is `null` — and
  returned only by `ValidateAll` and in the `DryRunResult.Issues` that `BuildPredictedSnapshot` returns,
  never by `ApplyAll`), where `IsOk` is `false` exactly when the duplicate includes an address
  AddressTeller itself would write this run (see `HasWritableDuplicate`), but the write still happens
  regardless — this status never blocks a write (see Added above). Code that filtered on `!result.IsOk` to
  decide whether an Apply should abort must exclude `DuplicateAddress` specifically, the way
  `Apply with Validate`'s own abort decision and `ApplyAllCLI`/`ApplyWithValidateCLI`'s exit-code logic
  already do; `Apply All` never runs Validate first and so has no abort decision to make on this basis, and
  `CheckCLI` is the one entry point that intentionally does not exclude it, since it never writes and its
  exit code 2 there is just a report signal, not an abort decision.
- **BREAKING**: `AddressTellerSettings.PostprocessOrder` no longer treats `0` as a reserved "unset"
  sentinel that falls back to `DefaultPostprocessOrder` (1000). If you had set it to `0` expecting it to
  be read back as `1000`, it is now read back and passed through as literal `0` to
  `AssetPostprocessor.GetPostprocessOrder()`, which runs this Postprocessor earlier than before relative
  to other packages' postprocessors. See
  [Apply & Operations: Project Settings](Documentation~/operations.md#project-settings).
- `TypeBasedRules` sample: addresses are now prefixed by asset type (e.g. `prefab/Player` instead of
  `Player`), so that two assets of different types sharing a file name in the same folder (e.g.
  `Player.prefab` and `Player.png`) no longer resolve to the same address and get reported as
  `ValidationStatus.DuplicateAddress`. Only affects the sample; only takes effect if you re-import it.
- **Upgrading from 0.5.x:** the first time this version runs in a project that doesn't yet have
  `ProjectSettings/AddressTellerSettings.json`, every setting listed in the settings-storage entry above
  resets to its default. Because **Auto-apply on import** and **Remove unmatched entries** both default
  ON, that default state is itself capable of running a destructive apply the moment it takes effect —
  and not only for whatever asset happens to trigger it: `AddressTellerPostprocessor`'s automatic apply
  writes addresses/labels, and (when `CleanupStaleEntries` is on) removes now-unmatched entries, only for
  the specific assets in that import — but whenever `CleanupStaleEntries` is on and no rule failed to
  configure this run, it also sweeps every entry already sitting in every group an enabled rule owns for
  structurally invalid paths, regardless of which assets were actually imported. A rule that just went
  from disabled back to enabled by this same reset changes what counts as "owned" for that sweep the
  moment it first runs. An asset import that reaches this
  Postprocessor does not require you to touch an asset yourself:
  recompilation after an IDE save, an Editor Auto Refresh after a VCS pull or branch switch regaining
  focus, or the updated package's own script files being imported as part of the update can each trigger
  one. Whether upgrading the package itself reliably triggers one of these before you get a chance to
  act is unconfirmed. AddressTeller itself does not trigger an asset import from
  `Project Settings > AddressTeller` — changing a value there only reads and writes
  `ProjectSettings/AddressTellerSettings.json`, which sits outside `Assets/`. What remains unconfirmed is
  whether something else (the Editor, another package, or the upgrade itself) causes an import to reach
  the Postprocessor before you get a chance to open that page.
  **Before upgrading:** note down your current values from `Project Settings > AddressTeller` — Auto-apply
  on import, Postprocessor order, Remove unmatched entries, Auto-create missing groups, Snapshot folder,
  Auto-snapshot before Apply, Auto-snapshot retention count, and the enable/disable state of any rule
  classes; save a manual snapshot (`Tools/AddressTeller/Snapshot/Save Snapshot`) as a restore point
  independent of the settings reset — it saves to (and, in `Tools/AddressTeller/Snapshot/Manage
  Snapshots...`, is only listed from) the current `Snapshot folder`, so if that setting is customized, the
  file itself is unaffected by the reset but won't show up in the UI again until you set `Snapshot folder`
  back to that value; and consider committing or otherwise backing up your Addressable Groups data
  (`Assets/AddressableAssetsData` by default) so a plain `git diff`/revert is available regardless of what
  the first post-upgrade apply does. **After upgrading**, before
  triggering any asset import yourself, open `Project Settings > AddressTeller` and re-enter your recorded
  values (Auto-apply on import and Remove unmatched entries first). The old `.asset` file is a Unity YAML
  file using the same field names as the new JSON's keys (see
  [Settings Asset](Documentation~/compatibility.md#7-settings-asset) for the list); if you didn't record
  your values beforehand, that file is your only remaining record of what they used to be, and you can
  still read them directly from it in a text editor as long as you haven't deleted it — this version never
  reads it back automatically either way, so it is safe to delete only once you've recorded its values (or
  no longer need them). For a team project, re-entering your values and
  committing the resulting `AddressTellerSettings.json` in the same commit that bumps the package may help
  teammates who pull that commit avoid ever running with AddressTeller's settings reset to defaults in
  memory on their own machine (which is what happens while `AddressTellerSettings.json` does not exist yet
  — no file is written until something saves a change), since the JSON would already exist with your
  values by the time they check out that commit — this has not been verified against the import-timing
  question above, so treat it as a suggestion rather than a guaranteed fix. If a destructive apply has
  already happened, `Tools/AddressTeller/Undo Last Apply` does not have a dedicated snapshot of that specific apply to
  restore, since the automatic apply on import never takes one — it restores to the most recent snapshot
  taken automatically before a manual `Apply All` / `Apply with Validate`, under the *current* `Snapshot
  folder` setting (if any exists there yet in this project — none will if `Snapshot folder` has reverted
  to its default and your auto-snapshots were saved under a custom one, until you set it back), in Exact
  mode, which undoes everything since that older snapshot together, not just this reset-triggered apply.

### Verified

- EditMode test suite: 656 tests (654 pass / 0 fail / 2 skip), run on Unity 6000.3.8f1 with Addressables 2.8.1. The minimum Addressables requirement remains 2.8.1.

## [0.5.0] - 2026-09-17

### Added

- `IAddressRuleGroupBuilder.IncludeFolders()` and `ILabelRuleBuilder.IncludeFolders()`:
  opt a rule into receiving folder assets during evaluation. Callable once per rule or group; calling
  twice throws `InvalidOperationException`. Without this call, folders are skipped entirely (predicate
  is never invoked).
- `AssetContext.IsFolder`: true when the asset is a folder rather than a file. Useful for rules that
  opt into folder evaluation and need to distinguish between the two; also available to tests and
  tooling that construct AssetContext manually.
- Overload of `AssetContext` constructor accepting `isFolder` parameter, in addition to the existing
  constructor. The existing constructor (without `isFolder`) defaults to `false`.
- `AddressRuleEntry.IncludesFolders`: read-only property indicating whether this entry opted in to
  folder evaluation. Not settable through the public `AddressRuleEntry` constructor — it is only ever
  `true` when the entry was produced by the builder (i.e. `IncludeFolders()` was called). Useful for
  code that constructs its own evaluation loop over collected entries (e.g. testing helpers).
- `ValidationStatus.EntryRejectedByAddressables`: reported when a rule matched, resolved an address,
  and the target group exists, but Addressables itself refused to create or move a usable entry for
  this asset (the path is not valid for an Addressables entry; Addressables returns no entry when the
  asset's main type also belongs to an editor assembly, and a read-only placeholder otherwise). This
  is distinct from `ValidationStatus.GroupNotFound`, which covers the target group itself not
  existing. Any read-only placeholder entry Addressables did create in this edge case is removed.

### Fixed

- Windows: `AddressableAssetSettings.ConfigFolder` may be returned with backslash separators. This
  meant that, on Windows, assets under the Config Folder were not actually excluded from rule
  evaluation, and `AddressTellerPostprocessor`'s early-exit check did not recognize changes confined to
  the Config Folder, so saving Addressables settings triggered an unnecessary incremental apply for the
  changed settings assets. The path is now normalized to forward slashes at the point of retrieval,
  restoring both behaviors; any Config Folder entries created while the exclusion was ineffective are
  removed by the invalid-path cleanup described below when `CleanupStaleEntries` is enabled.
- Apply no longer aborts with a `NullReferenceException` when Addressables' `CreateOrMoveEntry` returns
  `null`; the asset is now reported as `EntryRejectedByAddressables` instead.
- Read-only placeholder entries that Addressables silently creates for a path it considers invalid
  (when the asset's main type is not from an editor assembly) are no longer left behind; `Apply` now
  removes them.

### Changed

- **BREAKING**: Folders are now opt-in via `IncludeFolders()`. By default, folder assets do not appear
  in rule evaluation — neither the `Where()` predicate nor address/label selectors are invoked for
  folders. Rules that wish to register entries for folders must explicitly call `IncludeFolders()` on
  the builder (once per rule / `Group()` / `AnyGroup()`). Previously, every folder path returned by
  `AssetDatabase.GetAllAssetPaths()` was evaluated like a file, so a broad predicate (e.g.
  `Match.All()` or `Match.InFolder(...)`) could match a folder and create an Addressables folder entry
  that implicitly covers everything beneath it. Rules that do not call `IncludeFolders()` can now use
  broad predicates without matching folders. If `CleanupStaleEntries` is enabled, a folder entry that
  only existed because such a broad rule matched it before this change is treated as unmatched once
  the rule stops seeing it, and is removed by the same stale-entry cleanup that has always applied to
  unmatched file entries — this is not a new cleanup mechanism, and follows the same scoping as file
  cleanup (only assets actually evaluated by that particular apply are affected).
- **BREAKING**: Path validity checks now align with Addressables. Addressables itself rejects certain
  paths when users manually create entries via the Groups window or the Inspector "Addressable"
  checkbox: paths outside `Assets/` and outside a package's own folder (e.g. `ProjectSettings/`,
  `Library/`, `Temp/`); a package's own `package.json`; a package's own root folder with nothing
  beneath it; extensions `.preset` and `.asmdef`; paths containing `/Editor/` or ending in `/Editor`;
  the `Assets` root itself; and the Addressables Config Folder (see below). Such paths are now
  filtered out before rules run — no rule sees them and no result is reported for them, the same as
  any other pre-filtered path. `ValidationStatus.EntryRejectedByAddressables` is reported only if
  Addressables still refuses a write for a path that passed this filter (a rare case, see Added).
- **BREAKING**: `CleanupStaleEntries`, when enabled, also removes entries in managed groups whose
  asset path is structurally invalid for an Addressables entry at all — regardless of whether any rule
  matches them — for example leftovers created by an older AddressTeller version under
  `ProjectSettings/`, or with a `.preset`/`.asmdef` path. Unlike the stale-entry cleanup described
  above, this check scans every entry in every managed group on every apply, including import-time
  auto-apply, independent of which assets were actually imported or changed.
- **BREAKING**: Extension exclusion is now case-sensitive. File paths ending in `.CS`, `.DLL`,
  `.PRESET`, etc. (uppercase) are no longer automatically excluded from evaluation and will now pass
  through to rules. This matches Addressables' own behavior.
- **BREAKING**: The Config Folder exclusion now uses Addressables' own boundary-less prefix match,
  reverting the boundary-aware check introduced in 0.4.0. Folders that merely share the Config
  Folder's name prefix (e.g. `Assets/AddressableAssetsData_Backup`) are now excluded, matching the
  Groups window and Inspector; existing entries for assets under them — files as well as folders — in
  managed groups are removed on the next apply when `CleanupStaleEntries` is enabled.
- **Upgrading from 0.4.x:** with `CleanupStaleEntries` enabled (the default), the first apply after
  upgrading can delete existing entries in managed groups: folder entries (whether created by a broad
  rule or added by hand) that no rule opting in with `IncludeFolders()` matches; and entries whose path
  Addressables itself rejects (`.preset`/`.asmdef`, an `Editor` folder or anything under one, the
  `Assets` root, a package root or its `package.json`, paths outside `Assets/`/packages such as
  `ProjectSettings/`, and anything under or sharing a name prefix with the Config Folder). Invalid-path
  entries are removed by any apply, including the automatic apply on import, which does not take an
  automatic snapshot. Before upgrading: save a snapshot (`Tools/AddressTeller/Snapshot/Save
  Snapshot`), or temporarily disable `CleanupStaleEntries` and turn off auto-apply on import; add
  `IncludeFolders()` to rules that are meant to register folders; then run `Apply All` and review the
  removal warnings in the Console.
- `RuleUnitTestHelper` sample: `ExampleRuleTest.FindFirst` now skips a rule's `Predicate` for a folder
  `AssetContext` when the rule did not opt in via `IncludeFolders()`, matching the production
  evaluator's behavior; `RuleTestHelper`'s doc comments and the sample's README explain this for
  hand-rolled evaluation loops.

### Documentation

- `compatibility.md`: documented that adding a method to a rule-builder interface
  (`IAddressRuleBuilder`, `IAddressRuleGroupBuilder`, `ILabelRuleBuilder`) is a non-breaking change,
  since these interfaces are only ever implemented internally and are meant to be consumed, not
  implemented, by rule authors. Also documented the `IncludeFolders()` single-call constraint, that a
  folder never reaches a rule's `Where()` unless the rule opts in, and the stale-entry cleanup's
  expanded scope (structurally invalid paths, not just unmatched entries).
- `design-decisions.md`: documented the folder opt-in model, the rationale for aligning path validity
  checks with Addressables, and a design principle comparing AddressTeller's rule surface against what
  the Addressables Groups window allows and refuses manually.
- `operations.md`: documented the new path exclusion categories, the cleanup of path-invalid entries
  in managed groups, and a pointer to the upgrade note above.
- `writing-rules.md`: added guidance on `IncludeFolders()` use and folder context detection
  via `IsFolder`.

## [0.4.2] - 2026-08-07

### Documentation

- README.md, README.ja.md, Documentation~/writing-rules.md and .ja.md: corrected the asmdef
  reference guidance. A custom assembly that only defines rule classes needs `AddressTeller.Core`
  alone — the entire rule-authoring surface (`AddressRuleBase`, `IAddressRuleBuilder`, `Match`,
  `AssetCondition`, `Naming`, `AssetContext`, `RuleInspector`) lives there, as the
  `Rule Unit Test Helper` sample's own asmdef demonstrates. `AddressTeller.Editor` is required
  only when the assembly also calls the operational APIs (`AddressTellerService`,
  `ValidationResult`, snapshots, reports); referencing it always requires referencing
  `AddressTeller.Core` too, since those signatures expose Core types. The previous text told
  every rule assembly to reference both.
- README.md and README.ja.md: documented pinning the install to a release tag and clarified
  that the [Compatibility Policy](Documentation~/compatibility.md) takes effect only from `1.0.0`
  onward; while in `0.x`, breaking changes may land in minor releases, so tag pinning is strongly
  recommended. Noted that unpinned git URLs track the default branch, and that release tags exist
  only for version 0.4.0 and later.

## [0.4.1] - 2026-08-02

### Documentation

- README.md and README.ja.md: clarified that an asset matched by a rule is moved
  into that rule's group regardless of its current group, while deletions and
  label additions are limited to groups referenced by at least one rule.
- README.md and README.ja.md: added an Addressables primer (address / label /
  group, initializing Addressables) and expanded Quick Start with notes on entry
  creation and moving, managed-group scoping, and auto-apply on import —
  including that auto-apply also deletes entries that no longer match any rule
  (`CleanupStaleEntries`).
- Documentation~/operations.md and .ja.md: added a non-interactive CLI section
  covering the four CLI entry points' exit behavior, a batch-mode example for CI,
  and the fact that `Tools/AddressTeller/Clear All Addresses & Labels...` always
  shows a confirmation dialog with no dry-run gate.
- Documentation~/operations.md and .ja.md: added the previously undocumented
  `Tools/AddressTeller/Preview Group...` and
  `Assets/AddressTeller/Preview (Apply Preview)` entries to the Apply Methods table.
- Documentation~/operations.md and .ja.md: documented the per-rule preview
  ("Validate/Apply this rule only" button in Project Settings), including that it
  can predict `Removed` for entries owned by another rule targeting the same
  group, because only the selected rule is in evaluation scope.
- AddressTellerScopedPreview.cs XML doc comment: removed an inaccurate mention of
  sub-assets from the folder-expansion note on `RunGroupPreview`.

## [0.4.0] - 2026-08-01

### Added

- `ReportFormat` and `DistributionFormat` enums: new public types representing the format choices for report and distribution exports. `ReportFormat` supports `Json` and `Junit` (used by `AddressTellerReportWriter.WriteToFile`), while `DistributionFormat` supports `Csv` and `Markdown` (used by `BundleDistributionSerializer.WriteToFile`).
- `ValidationStatus.RuleConfigureFailed`: returned when a user rule's `Configure()` method throws an exception. The problematic rule is skipped (treated as producing no entries) and evaluation continues; this status helps identify which rule has a configuration problem.
- Warnings displayed in `Undo Last Apply` dialog and `Explain` window when rule configuration errors exist, so users are aware that reported results are incomplete.
- `AddressTellerReport.SchemaVersion`: a new field parallel to `AddressTellerSnapshot.SchemaVersion`, defaulting to 1 and set by `AddressTellerReportBuilder.Build`.
- `AddressTellerReport.CurrentSchemaVersion`: a new public constant (`= 1`) exposing the schema version `AddressTellerReport.SchemaVersion` defaults to and `FromJson` compares against. Previously this value existed only as an internal constant on `AddressTellerReportBuilder`, which meant it wasn't covered by the public API approval baseline; `AddressTellerReportBuilder` now reads this new public constant instead of defining its own.
- `AddressTeller.Testing.RuleInspector`: public API for inspecting rule configuration results without a real Addressables project. Provides `Collect()` to retrieve all rule entries registered by the rule, `IsUnresolvedDefaultGroup()` to check for unresolved `GroupDefault()` references, and `DisplayGroupName()` to format group names for display. Enables comprehensive rule unit tests; replaces the custom Fake builders previously used in `RuleUnitTestHelper` samples.

### Fixed

- `RestoreExactWithRemoval` API: `Undo Last Apply` now correctly deletes entries matching the confirmation dialog count, instead of leaving all entries intact.
- Rule collection no longer stops on constructor exceptions or open generic types; problematic rules are skipped and evaluation continues.
- Settings folder exclusion filter now includes path separators to prevent adjacent folders (e.g., `AddressableAssetsData_Backup`) from being mistakenly excluded.
- Cleanup no longer incorrectly deletes entries for assets matched by label-only rules; added `ValidationStatus.LabelsOnly` to properly track label-only rule matches.
- `CollectManagedGroups` now excludes `null` values and unresolved `GroupDefault()` sentinels from the managed groups list.
- `NullReferenceException` when `ApplyAll` is called with `paths: null`.
- Duplicate warnings and processing for the same group after `AutoCreateMissingGroups`.
- Potential `NullReferenceException` in the result window when `Context` is null.
- Snapshot file overwriting when multiple saves occur within the same second; improved error handling for write failures.
- Snapshot file helper duplication and folder boundary detection.
- `SnapshotFolder` path traversal vulnerability; added range check to restrict paths to the project directory.
- `Apply All` lacking progress bar display and cancellation support; integrated `EditorProgressReporter`.
- Incorrect comment in `Naming` class.
- Unnecessary array allocations in `AssetContext.PathSegments` by caching.
- Label-only rules incorrectly writing labels to unmanaged group entries without ownership check; labels are now restricted to managed groups.
- Automatic safety snapshot save failures were previously ignored; `Apply All` / `Apply with Validate` now properly handle these errors by stopping the operation and notifying the user.
- Snapshot save operation now handles `Directory.CreateDirectory` failures (e.g., invalid path or permission error) gracefully, preventing unhandled exceptions.
- Snapshot restoration now tolerates duplicate group names instead of throwing an exception; duplicate groups are reported as warnings. `AddressTellerSnapshotService.Restore` and `BundleModeReader` have been updated to handle this gracefully.
- `AssetFilter.ShouldExcludeByPath`: added null check before calling `path.Replace()` to prevent `NullReferenceException`.
- Snapshot JSON loading (`LoadFromFile`): extended mandatory field validation to include `GroupName` and `Entries` (in addition to the existing `Guid` check).
- Project Settings UI: Postprocessor order field now displays the effective clamped value (e.g., 0 becomes 1000) when the user leaves the field or presses Enter.
- `ExportDistribution`: now displays an error dialog when file write fails, instead of silently suppressing the error.
- `AddressTellerSnapshotService.Restore`/`RestoreExactWithRemoval`/`Diff` now throw `ArgumentNullException` for missing required arguments instead of an unguarded `NullReferenceException`, matching `AddressTellerClearService.Clear`'s existing contract. The package's null-argument policy (explicit entry points throw; methods with default-value fallback such as `AddressTellerService.*` do not) is now documented in the relevant XML doc comments.
- `AddressTellerSettings.DisabledRuleClassNames` now returns a defensive copy instead of the internal list instance, so mutating the returned collection can no longer affect the persisted setting.

### Changed

- **BREAKING**: Core domain model and evaluation engine (`Editor/Core/` content) are now split into an independent assembly `AddressTeller.Core`. The asmdef name is `AddressTeller.Core`, and the namespace remains `AddressTeller` (unchanged). If your project has a custom asmdef that references `AddressTeller.Editor`, you must also add `AddressTeller.Core` to its `references`; since public APIs in `AddressTeller.Editor` expose Core types (e.g., `ValidationResult.Context` is `AddressTeller.AssetContext`, `AddressTellerService.ApplyAll(..., rules)` accepts `IReadOnlyList<AddressTeller.AddressRuleBase>`), the compiler requires both assemblies in `references`.
- Validation notifications with `IsOk=true` (e.g., `GroupWillBeCreated`) are now logged as `Warning` instead of `Error` across all entry points (Postprocessor, Menu, ApplyFlow). This distinguishes informational status messages from actual errors.
- `AddressTellerMenu.Validate()` now opens the result window (showing Issues tab) when one or more errors (`IsOk=false`) are detected. Previously, results were only logged to the console.
- **BREAKING**: `BundleModeReader.ReadBundleModes()` and `BundleDistributionSummarizer.Build()` now include an `out` parameter for reporting duplicate group name warnings. Callers must accept this new parameter.
- **BREAKING**: `IAddressRuleBuilder.Address()` now throws `InvalidOperationException` when called twice on the same rule, matching `Where()` behavior. Previously, duplicate address assignments were silently overwritten; this change ensures early detection of ambiguous address specification.
- `ApplyAll`, `ValidateAll`, and `BuildPredictedSnapshot` now skip stale-entry cleanup (DeletedAssets tracking) if any rule configuration error is detected, preventing incorrect deletions of entries managed by broken rules.
- `ApplyAll` / `Apply with Validate` menu operations now cancel instead of continuing when automatic safety snapshot save fails, matching the fail-fast design of `ClearAll`.
- `ClearAll` menu and `ClearCLI` command now abort when rule configuration errors exist (CLI exits with code 3).
- **BREAKING**: `AddressTellerReportWriter.WriteToFile` and `BundleDistributionSerializer.WriteToFile` now take `ReportFormat` / `DistributionFormat` enum arguments instead of raw strings (`"json"`/`"junit"` and `"csv"`/`"markdown"`). CLI text arguments (`-addressTellerReportFormat`) are unaffected; only the public API surface changed.
- **BREAKING**: `SnapshotDiff.Added`/`Removed`/`Changed` are now `IReadOnlyList<T>` instead of `List<T>`. Code that mutated these collections directly must be updated.
- **BREAKING**: `SnapshotDiff` and `DryRunResult` parameterless public constructors are now `internal`. These types are instantiated only by the library's snapshot and dry-run APIs; tests continue to construct them via `InternalsVisibleTo`.
- **BREAKING**: `ValidationResult` and `AddressCandidate` public constructors are now `internal`. These types are only ever constructed by the library's own rule evaluation pipeline; tests continue to construct them via `InternalsVisibleTo`.
- **BREAKING**: `AddressTellerPostprocessor` is now `sealed`.
- **BREAKING**: `AddressTellerService.RemoveEntriesForDeletedAssets` now returns `IReadOnlyList<ClearedEntry>` (previously `void`), reporting the entries actually removed. This matches the convention already used by `ApplyAll`/`ValidateAll` of surfacing results instead of discarding them.
- **BREAKING**: `AddressTellerExplainReport`, `AddressTellerExplainAsset`, and `AddressTellerExplainRule` are now `internal` (previously `public`). No supported code path constructs or exposes these types outside the package.
- **BREAKING**: `AddressTellerCliArgs.ReportFormat` is now `ReportFormat?` instead of `string`. Code that read this property as a raw string (`"json"`/`"junit"`) must be updated to compare against the `ReportFormat` enum.
- `RuleUnitTestHelper` sample: `DefaultGroupSentinel` constant removed; `Collect()` now delegates to the package's own `RuleInspector` public API, ensuring the exact same builder contract (calling `Where()`/`Address()` a second time on the same group now throws `InvalidOperationException`). New helper methods `IsUnresolvedDefaultGroup()` and `DisplayGroupName()` added for checking and displaying unresolved default group sentinels in tests.
- **BREAKING**: `LogicalBundleDto` renamed to `BundleDistributionReportEntry`. This is a C# API-only rename; the JSON report output (field names) is unchanged.
- Enum members of `ValidationStatus`, `ClearScope`, `ReportFormat`, `DistributionFormat`, `SnapshotRestoreMode`, and `BundleModeKind` now carry explicit numeric values in source. This doesn't by itself enforce the member-to-number freeze described in [Compatibility Policy](Documentation~/compatibility.md#enums) — nothing prevents a future edit from renumbering — but the public API approval baseline now records each member's name and value, so `PublicApiApprovalTests` catches an accidental rename, removal, or renumbering. No behavior change (the implicit numbering was already sequential from 0).
- **BREAKING**: `ClearScope` enum values swapped: `Managed` is now `0` and `All` is now `1` (previously `All = 0`, `Managed = 1`). This aligns `default(ClearScope)` with the package's "destructive operations default to the safe side" principle. Code that reads `ClearScope` by its underlying numeric value (rather than by member name) must be updated; code that only refers to `ClearScope.All`/`ClearScope.Managed` by name is unaffected. No CLI or menu entry point had a reachable code path that relied on the previous `default(ClearScope)` value.
- **BREAKING**: `AddressTellerReport.FromJson` now rejects a report whose `SchemaVersion` is greater than `AddressTellerReport.CurrentSchemaVersion`, returning `null` and logging a warning instead of returning a report of an unrecognized shape. Callers must now null-check the result. Only this rejection policy mirrors `AddressTellerSnapshotService.LoadFromFile`'s existing rejection of snapshots from a newer schema — unlike `LoadFromFile`, `FromJson` does not validate the JSON's content and does not catch exceptions from the underlying `JsonUtility` call (see [Compatibility Policy](Documentation~/compatibility.md#5-report-output-json--junit-xml) for the precise differences).

### Documentation

- Added English versions of README.md and all Documentation~ files. The original Japanese content is preserved as `.ja.md` files (e.g., `README.ja.md`, `architecture.ja.md`). Each file includes a language switch link at the top.
- Converted CONTRIBUTING.md to English; Japanese version saved as CONTRIBUTING.ja.md.
- Converted CHANGELOG.md to English; Japanese version saved as CHANGELOG.ja.md.
- `writing-rules.md`: added Testing section with guidance on using `RuleInspector` API and the `RuleUnitTestHelper` sample for unit-testing rule classes.
- `operations.md`: added `RuleUnitTestHelper` to the Samples section.
- `AddressTellerSettings.CleanupStaleEntries` XML doc and `design-decisions.md`: corrected misleading text that incorrectly stated "labels are not deleted." Both addresses and labels are removed from stale entries.
- Converted all public API XML documentation comments to English, and documented previously undocumented public members (IntelliSense text is now English).
- Added [Compatibility Policy](Documentation~/compatibility.md), listing the public C# API, CLI entry points/arguments, exit codes, report/snapshot/settings file formats, menu paths, and rule-authoring behavior covered by SemVer guarantees, along with the open-enum contract for `ValidationStatus` and friends.
- `CONTRIBUTING.md`: added a Type Naming section documenting when public types take the `AddressTeller` prefix (entry points and serialized artifact roots only) and the naming exception for the rule-authoring DSL (`Match`, `Naming`, etc.).
- `operations.md`: fixed the `BundleDistribution` JSON section description, which previously showed the key names in the wrong casing (`bundleDistribution`, `totalLogicalBundleCount`, `unknownGroupCount`) — `JsonUtility` does not apply any casing convention, so the actual output uses the C# field names verbatim (`BundleDistribution`, `TotalLogicalBundleCount`, `UnknownGroupCount`). CI parsers written against the old (incorrect) casing should be updated.
- `operations.md`: documented that `PostprocessOrder`'s `0` is a reserved "unset" sentinel — explicitly setting the field to `0` is treated the same as leaving it unset and falls back to `1000`.
- `writing-rules.md`: noted that a rule file also using `System.Text.RegularExpressions` should add `using Match = AddressTeller.Match;` to disambiguate the two `Match` types.
- `operations.md`: documented the two exit-code-3 conditions added for `-addressTellerDisableRules`/`ClearCLI` — an unknown rule class name passed to `-addressTellerDisableRules`, and (for `ClearCLI` with `scope=managed`) a rule configuration error that makes `managedGroups` untrustworthy.
- Added [Compatibility Policy](Documentation~/compatibility.md) documentation of the `BundleDistribution` report section's "always present" semantics: the field is never omitted or JSON `null`; when it could not be calculated (no `DryRunResult.After`, no `AddressableAssetSettings` supplied, or the calculation itself threw), it appears as an all-C#-defaults object (`Bundles: []`, counts at `0`, empty `Disclaimer`) rather than being left out.

### Verified

- Addressables 2.8.1 through 3.1.0 compatibility was confirmed in prior work (353 EditMode tests at that time).
- Current EditMode test suite: 502 pass / 0 fail / 2 skip (504 total). The minimum Addressables requirement remains 2.8.1.

---

## [0.3.0] - 2026-06-14

### Added

- `IAddressRuleBuilder.GroupDefault()`: use instead of `Group("name")` to assign addresses and labels to the Addressables `DefaultGroup`. The group is resolved from `AddressableAssetSettings.DefaultGroup` at evaluation time, so it follows DefaultGroup renames automatically. `Where`/`Address`/`Label` chain the same way as `Group()`.
- `ValidationStatus.DefaultGroupUnavailable`: returned when a `GroupDefault()` rule exists but `AddressableAssetSettings.DefaultGroup` cannot be resolved. The write for the affected asset is skipped (`IsOk = false`).
- Project Settings: added "Postprocessor execution order" setting (`AddressTellerSettings.PostprocessOrder`, default 1000). `AddressTellerPostprocessor.GetPostprocessOrder()` returns this value to control execution order relative to other `AssetPostprocessor`s.
- CLI: added `-addressTellerDisableRules <FullName>[,...]` to `ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI`. The specified names are temporarily excluded (unioned with the persistent Project Settings disabled list) for that CLI run only — intended for excluding debug-only rules in CI. If any specified FullName does not match a known rule class, the command exits with code 3. This exclusion is CLI-only and does not affect the Postprocessor or menu.
- Project Settings: added a collapsible "Managed Groups" list showing the group names referenced by enabled rules — the groups targeted by `CleanupStaleEntries`/`AutoCreateMissingGroups`. The description notes that manually registered entries inside these groups are subject to deletion if no rule matches them.

### Documentation

- Added missing `CheckCLI` entry to the CI integration section of Documentation~/operations.md. Clarified that the exit code table applies to all three CLI methods (`ApplyAllCLI`/`ApplyWithValidateCLI`/`CheckCLI`).
- Fixed incorrect description of `CleanupStaleEntries` (Project Settings UI and operations.md) that stated "labels are not deleted." Entry deletion via `RemoveAssetEntry` removes both the address and Addressables labels.
- Added to `design-decisions.md`/`operations.md`: manually registered entries inside a managed group are also subject to `CleanupStaleEntries` deletion if no rule matches them. Previously only the "unmanaged groups are untouched" guarantee was documented; the reverse was not.

### Changed

- Reordered Project Settings sections to "Apply & Validate behavior → Registered rules → Operations → Snapshots" so the most frequently checked "Registered rules" list is no longer cut off at the bottom of the screen.
- Renamed the "Auto-apply" section heading to "Apply & Validate behavior." `CleanupStaleEntries`/`AutoCreateMissingGroups` affect all entry points (Apply All / Validate / CLI / snapshot dry-run), not just import-time auto-apply, so the old heading was misleading.
- Changed the default scope of `Tools/AddressTeller/Clear All Addresses & Labels...` (menu) and `ClearCLI` (when `-addressTellerClearScope` is not specified) from `All` (all entries) to `Managed` (entries in AddressTeller-managed groups only). Use `-addressTellerClearScope all` to clear all entries.
- **BREAKING**: Changed root namespace from `Natsume777.AddressTeller` to `AddressTeller`. Users must replace `using Natsume777.AddressTeller;` with `using AddressTeller;`. The editor assembly was also renamed from `Natsume777.AddressTeller.Editor` to `AddressTeller.Editor`; update `references` in any asmdef that references it.
- Reorganized `Editor/Application/` and Snapshot-related EntryPoints into feature-based subfolders (`Snapshot/` / `Reporting/` / `Bundle/`). Internal structure only — no public API impact.
- Package name (`com.natsume777.addressteller`) is unchanged.

## [0.2.0] - 2026-06-14

### Added

- Rule enable/disable toggles in the Project Settings rule list. The enabled/disabled state is saved to `ProjectSettings/AddressTellerSettings.asset`. Disabled rules are excluded from `Apply All` / `Validate` / `Apply with Validate` / `Explain` / snapshot dry-run predictions. Note: stale-entry cleanup (ownership determination) always considers all rules regardless of the toggle, so entries previously managed by a disabled rule are still tracked correctly.
- `Match` static class: helpers for building condition predicates. Provides `InFolder(string)` / `OfType<T>()` / `Glob(string)`, composable with `And(AssetCondition)`. Each helper auto-generates a human-readable description (e.g., `"InFolder(Assets/Characters)"`) shown in the Explain window and error messages. `All()` returns an unconditional match for rules without filters.
- `Naming` static class: common address generation patterns. Provides `FileName()` / `FileNameWithoutExtension()` / `ParentFolderName()` / `RelativePath(string root)` for use with `Address()`. Handles path normalization so you don't have to.
- New `AssetContext` properties: `Extension` / `IsInFolder(string)` / `PathSegments` / `RelativePathFrom(string root)`. Useful for concise path analysis in raw `Where()` lambdas.
- Auto safety snapshot: automatically saves the current state to `SnapshotFolder/Auto` immediately before `Apply All` / `Apply with Validate` menu execution, with configurable rotation (default 10 snapshots). `Tools/AddressTeller/Undo Last Apply` restores from the latest auto-snapshot in Exact mode (effectively an undo for `CleanupStaleEntries` deletions etc.). Configurable in Project Settings (default ON). Not applied to CLI (`ApplyAllCLI`/`ApplyWithValidateCLI`).
- `AddressTellerSnapshotService.BuildPredictedSnapshot`: dry-run API that computes the post-apply diff (additions, changes, deletions) and issues (conflicts, missing groups, rule exceptions) without writing anything. Returns `DryRunResult` (`SnapshotDiff` + `IReadOnlyList<ValidationResult>`).
- `IProgressReporter` overloads for `AddressTellerService.ApplyAll` / `ValidateAll`. `EditorProgressReporter` shows a cancelable progress bar via `EditorUtility.DisplayCancelableProgressBar`; cancellation returns results up to the point of cancellation without rollback.
- Apply confirmation dialog for `Tools/AddressTeller/Apply All` / `Apply with Validate` menu: runs `BuildPredictedSnapshot` first and shows "added N / changed N / ⚠ deleted N / issues N" with three options (Apply / Cancel / Show details). Skips the dialog and shows only a log when there are no diffs or issues. "Show details" opens the result window and allows Apply from the same result set. If `Apply with Validate` detects issues at the Validate step, it goes directly to the result window (Issues tab) without the dialog. Not applied to CLI.
- `Tools/AddressTeller/Explain` menu: evaluates all rules against the selected asset in the Project window and displays the results in a window — matched rules (address/labels assigned), unmatched rules (with `Where` description), and rule exceptions. Works best with `Where(predicate, description)` to get readable descriptions.
- `Tools/AddressTeller/Snapshot/Manage Snapshots...` menu: unified window for listing, restoring, and comparing saved snapshots. Records metadata (timestamp, user comment, Unity/package version, schema version) and validates JSON on load (unsupported schema, missing/duplicate GUIDs, etc.). Replaces the previous individual menu items (`Restore Snapshot (Additive)`, `Compare with Current State`, etc.), which are removed from the menu.
- Rules injection overload for `AddressTellerService.ApplyAll` / `ValidateAll`: accepts `IReadOnlyList<AddressRuleBase>` directly without going through reflection-based collection. The existing reflection overload delegates internally to this new one, remaining backward compatible.
- `ToJson()` for Explain results (`AddressTellerExplainReport` / `AddressTellerExplainAsset` / `AddressTellerExplainRule`). Results can be consumed by external tools the same way as `AddressTellerReport` / `AddressTellerSnapshot`.

### Changed

- Moved settings storage from `EditorPrefs` to `ProjectSettings/AddressTellerSettings.asset` for team sharing. Existing settings are not migrated and must be reconfigured.
- Changed import-time auto-apply to differential mode — only changed or moved assets are processed. Full-project consistency checks remain the responsibility of `Apply All` / `Validate` / CLI.
- Changed the diff display for `Tools/AddressTeller/Snapshot/Compare with Current State...` and `Compare Two Snapshots...` from `Debug.Log` output to a Diff tab in the result window.
- Internalized many types without affecting the public DSL surface (`AddressRuleBase` / `IAddressRuleBuilder` / `Match` / `Naming` / `AssetContext`). Internalized types include: `AddressRuleBuilderImpl` / `RuleEvaluator` / `RuleExplanation` / `RuleMatchOutcome` / `RuleEvaluationDetail` (DSL internals); `AddressTellerApplier` / `RuleCollector` / `AssetFilter` / `RuleExplainService` / `SnapshotFileCatalog` / `AddressTellerAutoSnapshotService` / `AddressTellerReportBuilder` / `AddressTellerExplainReportBuilder` (Addressables integration internals); `AddressResolution` / `RuleEvaluationError` (rule evaluation internal result types). `AddressCandidate`, exposed via the public API `ValidationResult.ConflictingCandidates`, remains public.
- Reorganized documentation: README now serves as an overview with links (requirements, installation, quick start, docs). DSL reference, operations guide, design decisions, testing guidelines, and architecture are moved to `Documentation~/` and `CONTRIBUTING.md`.

## [0.1.0] - 2026-06-08

### Added

- Rule definition DSL: classes inheriting `AddressRuleBase` are collected automatically from assemblies; rules are defined by chaining `Group().Where().Address().Label()` inside `Configure(IAddressRuleBuilder)`.
- Rule evaluation engine: evaluates all rules in ascending `Order`; two or more address candidates result in a conflict error; labels accumulate from all matching rules.
- `Tools/AddressTeller/Apply All` / `Validate` / `Apply with Validate` menus and CI-oriented `ApplyAllCLI` / `ApplyWithValidateCLI`.
- `AssetPostprocessor`-based auto-apply on import, move, and delete (can be toggled in Project Settings).
- Duplicate `Order` value warning when multiple rule classes share the same order.
- `CleanupStaleEntries`: automatically removes entries for assets that no longer match any rule from AddressTeller-managed groups.
- Snapshot feature: save, restore (Additive / Exact), and compare the current Addressables state (groups, addresses, labels) as JSON.
- Project Settings UI: auto-apply toggle, `CleanupStaleEntries` toggle, snapshot folder setting, and registered rule list.
