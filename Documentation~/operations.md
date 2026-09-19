[日本語](./operations.ja.md)

# Apply & Operations

## Apply Methods

| Method | Description |
|---|---|
| Auto-apply on import | `AssetPostprocessor` automatically runs the equivalent of `Apply All` whenever an asset is imported, moved, or deleted. Can be disabled in Project Settings. |
| `Tools/AddressTeller/Apply All` | Manually applies rules to the entire project. |
| `Tools/AddressTeller/Preview Group...` | Shows a dropdown of existing groups; picking one runs a dry-run of all enabled rules starting from that group's current members (folders are expanded), and opens the result window (Diff/Issues, plus Distribution when it can be computed) without writing anything. Apply All or Validate must be run separately afterward. |
| `Tools/AddressTeller/Validate` | Outputs conflicts, missing groups, and other issues to the Console without writing any changes. |
| `Tools/AddressTeller/Apply with Validate` | Runs Validate first and aborts Apply if any issues are found. |
| `Assets/AddressTeller/Explain` (right-click menu in the Project window) | Evaluates all rules against the selected asset and displays the results in a confirmation window. Shows matched rules, unmatched rules (with their `Where` description), and rule exceptions. Rules using `Match` helpers display auto-generated descriptions (e.g., `InFolder(Assets/Characters) AND OfType<GameObject>`), making behavior verification more efficient than raw lambdas. |
| `Assets/AddressTeller/Preview (Apply Preview)` (right-click menu in the Project window) | Runs a dry-run of all enabled rules against the selected assets (folders are expanded recursively) and opens the same result window as `Preview Group...`, without writing anything. |
| `Tools/AddressTeller/Clear All Addresses & Labels...` | Removes Addressable entries (addresses, group assignments, and labels) from AddressTeller-managed groups only. Requires saving a dedicated snapshot (`SnapshotFolder/Clear`, excluded from rotation) before execution, followed by a confirmation dialog. Intended as a pragmatic tool for initial setup of a pre-release package. Deleted entries are logged individually (Warning) to the Console and can be restored via Snapshot Restore. |

## CI Integration

The following methods can be invoked via `-executeMethod`:

- `AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI`
- `AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI` (runs Validate first and aborts Apply if any issues are found)
- `AddressTeller.Editor.AddressTellerMenu.CheckCLI` (dry-run without Apply — detects drift and issues in read-only mode)
- `AddressTeller.Editor.AddressTellerMenu.ClearCLI` (removes entries from AddressTeller-managed groups by default; pass `-addressTellerClearScope all` to remove all Addressable entries in the project instead; requires `-addressTellerConfirmClear`)

Specifying `-addressTellerReport <path>` / `-addressTellerReportFormat json|junit` outputs a structured report file: `CheckCLI` reports its dry-run results; `ApplyAllCLI` / `ApplyWithValidateCLI` report the pre-apply diff (dry-run). If `-addressTellerReportFormat` is omitted, the format is `junit` when the extension is `.xml`, otherwise `json`.

Specifying `-addressTellerFailOnSettingsMismatch` makes all four CLI entry points check, before doing anything else, whether `ProjectSettings/AddressTellerSettings.asset` on disk matches the settings currently loaded in memory (the same check as the startup diagnostic described under [Project Settings](#project-settings) below, run on demand instead of waiting for the next Editor session). If it does not match, the run logs an error and exits with code 3 instead of proceeding — useful for catching a settings file that failed to load (see the "Changed" entry for this version in [CHANGELOG.md](../CHANGELOG.md)) before it can affect a CI run's outcome. This flag does not control whether the underlying check runs at all — the once-per-session startup diagnostic below runs unconditionally, including under `-batchmode`, regardless of this flag; the flag only controls whether a mismatch also fails the CLI run. Consequently, a CI run that both has a mismatch and passes this flag logs the same differing fields twice (once as the startup `Debug.LogWarning`, once as the CLI's own `Debug.LogError`) and writes the comparison temp file twice — worth knowing if something downstream parses the log mechanically. Omitting this flag leaves all four CLI entry points' behavior unchanged.

Exit codes (`ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI`):

| Exit code | Meaning |
|---|---|
| 0 | No drift, no issues |
| 1 | Drift detected (changes present, no Validation errors) |
| 2 | Validation errors present |
| 3 | Environment error (`AddressableAssetSettings` missing, invalid arguments, an unknown rule class name in `-addressTellerDisableRules`, report write failure, or — only when `-addressTellerFailOnSettingsMismatch` is specified — a settings file/memory mismatch) |

`ClearCLI` exit codes:

| Exit code | Meaning |
|---|---|
| 0 | Clear completed |
| 3 | Environment error (`AddressableAssetSettings` missing, invalid arguments, a rule configuration error that makes `managedGroups` untrustworthy for `scope=managed`, snapshot save failure, or — only when `-addressTellerFailOnSettingsMismatch` is specified — a settings file/memory mismatch) |
| 4 | Rejected because `-addressTellerConfirmClear` was not specified (intentional rejection) |

### Running from the Command Line

None of the four CLI entry points above show a confirmation dialog or otherwise wait for input: each one parses its arguments, runs, logs the result, and always finishes by calling `EditorApplication.Exit(<code>)` itself. This holds even for `ClearCLI`, which performs a destructive operation — in a CLI context it substitutes the missing confirmation dialog with the required `-addressTellerConfirmClear` flag (see the exit code table above) rather than blocking for input. (Contrast this with the equivalent interactive menu items. `Apply All` and `Apply with Validate` run a dry-run first and only show a confirmation dialog when there is anything to apply — if the dry-run finds no changes and no issues, they return without showing a dialog at all (`AddressTellerApplyFlow`). `Clear All Addresses & Labels...` does not dry-run at all and always shows a confirmation dialog, even when there is nothing to remove — it only skips the dialog if a rule configuration error makes managed-group ownership untrustworthy, in which case it aborts before reaching the dialog. See [Apply Methods](#apply-methods) above.)

Example: running `ApplyAllCLI` headless, writing a JSON report, and using the process exit code to decide pass/fail:

```
"<path-to-Unity-executable>" -batchmode -quit -projectPath "<path-to-project>" -executeMethod AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI -addressTellerReport report.json -logFile -
```

- `-batchmode` runs Unity headless. `-quit` is Unity's own convention for exiting once `-executeMethod` returns; in practice each CLI method above already calls `EditorApplication.Exit(<code>)` itself before returning (see above), so `-quit` mainly serves as a safety net in case that call is ever skipped.
- `-logFile -` streams the Editor log to stdout instead of a file, which is useful for capturing this package's `Debug.Log` / `Debug.LogError` output in CI.
- Swap in `CheckCLI`, `ApplyWithValidateCLI`, or `ClearCLI` (which additionally requires `-addressTellerConfirmClear`, and optionally `-addressTellerClearScope all`) as needed.
- Inspect the exit code against the tables above to decide whether the run should fail the build.
- `AddressTellerSettings.SaveToDisk()` and `ReloadFromDisk()` (see [Project Settings](#project-settings)
  below) never emit a log that originates from AddressTeller itself unless something is genuinely wrong:
  `SaveToDisk()` only logs (`Debug.LogError`) when writing the file or verifying the write fails. It may
  also, rarely, log a one-off `Debug.LogWarning` if it fails to clean up the temporary file it uses for
  that verification — this does not change the return value; the temporary file is simply left behind.
  `ReloadFromDisk()` never logs on its own. If the settings file is corrupted, though, Unity's own
  deserializer may log a parse error while `ReloadFromDisk()` reads it — that log comes from Unity, not
  from AddressTeller, but it will still appear in a `-logFile -` capture, and the settings silently fall
  back to their defaults in that case (see [Project Settings](#project-settings) below).
  This does not extend to the settings load diagnostic described under [Project Settings](#project-settings)
  below (and, in a CI run, to `-addressTellerFailOnSettingsMismatch` above): that diagnostic exists
  specifically to surface a settings file/memory mismatch, so it logs a `Debug.LogWarning` (or, with that
  flag, a `Debug.LogError` followed by exiting) whenever it finds one — this is separate from, and not
  governed by, the "silent unless something is wrong with the write/read itself" contract described for
  `SaveToDisk()` / `ReloadFromDisk()` above. That diagnostic reuses the same temp-file re-serialization
  helper as `SaveToDisk()`, though, so it can likewise, rarely, emit the same unrelated one-off
  `Debug.LogWarning` about failing to clean up its temp file — described two paragraphs above — even when
  the file and memory otherwise match.

### Logical Bundle Distribution Summary

The `json` report includes a `BundleDistribution` section (JSON keys mirror the C# field names verbatim, since `JsonUtility` does not apply any casing convention). This is an estimate of logical bundle units computed from the dry-run Predict results (asset → group/labels) and each group's BundleMode (PackTogether/PackSeparately/PackTogetherByLabel). It is intended as a quick check for unintended extreme distributions (e.g., one huge bundle or hundreds of tiny ones), and **does not guarantee accuracy against an actual Addressables build**.

Known approximation differences:

- PackTogether's scene-level bundle splitting
- PackSeparately's per-folder grouping
- PackTogetherByLabel label combination differences (this summary uses sorted label sets joined with a separator as a normalization key)

Groups without `BundledAssetGroupSchema` cannot have their BundleMode determined and are treated as `Unknown`; they are excluded from `TotalLogicalBundleCount` and counted separately in `UnknownGroupCount`. See [Compatibility Policy](compatibility.md#5-report-output-json--junit-xml) for the full list of JSON keys and their stability guarantees.

## Project Settings

**If you are upgrading from a version prior to this one**, every value on this screen is silently reset
to its default the first time this version loads `ProjectSettings/AddressTellerSettings.asset` — the reset
itself is not accompanied by a warning or error. Note down your current values below before upgrading, and
re-apply them afterward. See the "Changed" entry for this version in [CHANGELOG.md](../CHANGELOG.md) for
the confirmed details and the settings this affects.

Starting with this version, though, you don't have to catch this purely by memory: once per Editor
session — not repeated on every domain reload within that session — AddressTeller compares
`ProjectSettings/AddressTellerSettings.asset` on disk against a fresh re-serialization of the settings
currently loaded in memory, and — if they differ — logs a `Debug.LogWarning` listing the differing fields
by their serialized name (e.g. `_postprocessOrder`), along with guidance on what to do next. This check
itself logs nothing about the comparison whenever the file does not exist yet, cannot be read, or matches
memory (see the temp-file-cleanup caveat under [Running from the Command Line](#running-from-the-command-line)
above for the one unrelated exception), and it never runs during asset import, so it adds no per-import
cost. This startup check runs unconditionally, including in `-batchmode` CI runs. In CI, pass
`-addressTellerFailOnSettingsMismatch` (see [CI Integration](#ci-integration) above) to additionally fail
the run on a mismatch instead of only logging a warning — that flag does not control whether the check
itself runs.

Under `Project Settings > AddressTeller`:

- **Auto-apply on import** (default: ON) — When off, `AssetPostprocessor` auto-apply is disabled. Manual menu operations are unaffected.
- **Postprocessor execution order** (`PostprocessOrder`, default: 1000) — Passed to `AssetPostprocessor.GetPostprocessOrder()`. Lower values run before other `AssetPostprocessor`s. The high default puts AddressTeller after other packages' postprocessors, so assets generated or modified by those run first. Note: `0` is reserved as the "unset" sentinel — explicitly setting the field to `0` is treated the same as leaving it unset and falls back to `1000`.
- **Remove unmatched entries** (`CleanupStaleEntries`, default: ON) — On apply (a manual `Apply All`, and the automatic apply on import for the changed assets), removes assets from AddressTeller-managed groups (groups referenced by at least one rule) that no longer match any rule. Deletion is per-entry (`RemoveAssetEntry`), removing both the address and Addressables labels. Entries in groups AddressTeller does not manage are never touched. **However, manually registered entries inside a managed group will be deleted if no rule matches them** (only per-asset matching is checked). When enabled, this also removes entries in managed groups whose asset path is structurally invalid for an Addressables entry (not just entries no longer matched by any rule) — for example leftovers created by an older AddressTeller version. Entries whose path cannot currently be resolved at all (empty `AssetPath`, e.g. an unfetched LFS pointer, an in-progress branch switch, or a missing package) are left alone by this check; a genuinely deleted asset is instead handled by the separate deletion-notification path. This check runs against every entry currently sitting in a managed group, independent of which assets were actually imported/changed, so it also catches leftovers that an incremental Apply (e.g. the auto-apply-on-import Postprocessor) would otherwise never revisit — every Postprocessor run re-scans every entry in every managed group for this, not just the imported/changed assets. **If you are upgrading from an earlier version**, the first apply can remove existing managed-group entries that are newly considered invalid or unmatched — folder entries no rule opts into with `IncludeFolders()`, paths Addressables itself rejects, paths under or sharing a name prefix with the Config Folder, and so on. See the "Upgrading from 0.4.x" note in [CHANGELOG.md](../CHANGELOG.md) for the full list of affected categories and how to prepare before applying. See also [Design Decisions: Deletions Are Determined by Per-Asset Ownership](design-decisions.md#deletions-are-determined-by-per-asset-ownership) and [Design Decisions: Missing Groups Are an Error](design-decisions.md#missing-groups-are-an-error-default).
- **Snapshot folder** (see below)

These settings are saved to `ProjectSettings/AddressTellerSettings.asset`, which can be version-controlled and shared across team members. Each property setter already writes this file on change (assigning the value a property already has is a no-op and does not write). If the file and the in-memory settings ever drift apart — for example the file failed to load, or it was edited outside the Editor (a merge, a manual edit, a VCS checkout) while the Editor was already running — the property setters alone cannot recover, because the same-value check silently skips the write. `AddressTellerSettings.ReloadFromDisk()` reloads the file into memory by discarding the current in-memory settings object and letting Unity recreate it from disk (this changes the identity of the internal settings object; unsaved in-memory changes are lost); it returns `false` without touching memory only when the file does not exist or is not accessible. `AddressTellerSettings.SaveToDisk()` writes the current in-memory settings to the file unconditionally, even when no value changed (returns `false`, with a logged error, if writing or reading the file fails, or if the file read back afterward does not match a fresh re-serialization of the in-memory settings).

To recover from drift, decide which side you want to keep *before* calling either method — `ReloadFromDisk()` immediately overwrites memory with the file's values, so by the time it returns there is no way to get the pre-call in-memory values back:

- To discard the in-memory changes and adopt the file's values: call `ReloadFromDisk()` and stop there.
- To discard the file's contents and keep the in-memory values: do **not** call `ReloadFromDisk()` — call `SaveToDisk()` directly. (Calling `ReloadFromDisk()` first and then `SaveToDisk()` does not "restore" the in-memory values; it just writes the file's own values back to itself, since `ReloadFromDisk()` has already replaced them in memory.)

If the Project Settings window is open, it does not refresh automatically after either call — its fields still show whatever was displayed when the page was built. Close and reopen the window (or navigate away and back) before touching any field, otherwise editing a stale field will write its old, pre-reload value back over what you just loaded or saved.

The same screen shows the list of registered rule classes (`AddressRuleBase` subclasses) and their `Order` values. Each class has an enable/disable toggle for temporarily disabling specific rules during debugging or verification. Disabled rules are excluded from `Apply All` / `Validate` / `Apply with Validate` / `Explain` / snapshot dry-run predictions. Each rule row also has a "Validate/Apply this rule only" button that runs a dry-run of that single rule against every asset in the project and opens the result window; because only one rule is in scope, entry-removal predictions for entries owned by other rules are not shown (a notice to that effect appears in the result window). Conversely, if another rule also targets the same group, assets that only that other rule matches can appear as "Removed" in this single-rule preview even though `Apply All` would not remove them (since `Apply All` evaluates every rule together) — run `Apply All` or `Validate` afterward to see the final cross-rule result. This button ignores the enable/disable toggle: a disabled rule can still be dry-run individually by clicking it explicitly.

Note: stale-entry cleanup on asset deletion (`CleanupStaleEntries`, etc.) always considers all rules regardless of the enabled/disabled toggle. This ensures that entries previously managed by a now-disabled rule are still correctly tracked so orphaned entries do not accumulate.

## Snapshots

Via the `Tools/AddressTeller/Snapshot/` menu, you can save, restore, and compare the current Addressables state (groups, addresses, labels) as JSON.

- **Save Snapshot**: Saves the current state to a JSON file.
- **Restore Snapshot (Additive)**: Writes the snapshot contents back. Labels not in the snapshot are left as-is.
- **Restore Snapshot (Exact)**: Writes the snapshot contents back and strips any labels not in the snapshot to achieve an exact match.
- **Compare with Current State / Compare Two Snapshots**: Outputs additions, deletions, and changes to the Console.

The save folder can be changed in Project Settings (default: `AddressTellerSnapshots/` in the project root, outside Assets).

### Auto Safety Snapshot

When `Tools/AddressTeller/Apply All` or `Tools/AddressTeller/Apply with Validate` is run from the menu, the current state is automatically saved as a snapshot and managed with rotation. `Tools/AddressTeller/Undo Last Apply` restores from the latest auto-snapshot in Exact mode, allowing safe recovery from changes like `CleanupStaleEntries` deletions.

Configurable in Project Settings:

- **Save auto-snapshot before Apply** (default: ON) — When off, no auto-save occurs on menu execution.
- **Auto-snapshot retention count** (default: 10, minimum: 1) — Auto-snapshots older than the specified count are automatically deleted.

The auto-snapshot feature applies only to the `Tools/AddressTeller/Apply All` and `Tools/AddressTeller/Apply with Validate` menu items. It does not apply to import-time auto-apply or CLI (`ApplyAllCLI`/`ApplyWithValidateCLI`).

## Samples

The following samples can be imported from the Package Manager's Samples tab (`Samples~/`):

- **Basic Rules** — Minimal rule definition example.
- **Folder-based Rules** — Example that maps folder hierarchy directly to addresses and labels.
- **Type-based Rules** — Example that routes assets to groups and labels by asset type.
- **Rule Unit Test Helper** — Helper and NUnit sample for unit-testing `AddressRuleBase` subclasses without a live Addressables project.
