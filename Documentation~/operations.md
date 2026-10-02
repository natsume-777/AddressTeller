[日本語](./operations.ja.md)

# Apply & Operations

## Apply Methods

| Method | Description |
|---|---|
| Auto-apply on import | Off by default. When turned on in Project Settings, `AssetPostprocessor` automatically runs the equivalent of `Apply All` whenever an asset is imported, moved, or deleted. |
| `Tools/AddressTeller/Apply All` | Manually applies rules to the entire project. |
| `Tools/AddressTeller/Preview Group...` | Shows a dropdown of existing groups; picking one runs a dry-run of all enabled rules starting from that group's current members (folders are expanded), and opens the result window (Diff/Issues, plus Distribution when it can be computed) without writing anything. Apply All or Validate must be run separately afterward. |
| `Tools/AddressTeller/Validate` | Outputs conflicts, missing groups, and other issues to the Console without writing any changes. |
| `Tools/AddressTeller/Apply with Validate` | Runs Validate first and aborts Apply if any issues are found. |
| `Assets/AddressTeller/Explain` (right-click menu in the Project window) | Evaluates all rules against the selected asset and displays the results in a confirmation window. Shows matched rules, unmatched rules (with their `Where` description), and rule exceptions. Rules using `Match` helpers display auto-generated descriptions (e.g., `InFolder(Assets/Characters) AND OfType<GameObject>`), making behavior verification more efficient than raw lambdas. |
| `Assets/AddressTeller/Preview (Apply Preview)` (right-click menu in the Project window) | Runs a dry-run of all enabled rules against the selected assets (folders are expanded recursively) and opens the same result window as `Preview Group...`, without writing anything. |
| `Tools/AddressTeller/Clear All Addresses & Labels...` | Removes Addressable entries (addresses, group assignments, and labels) from AddressTeller-managed groups only. Requires saving a dedicated snapshot (`SnapshotFolder/Clear`, excluded from rotation) before execution, followed by a confirmation dialog. Intended as a pragmatic tool for initial setup of a pre-release package. Deleted entries are logged individually (Warning) to the Console and can be restored via Snapshot Restore. |

Before writing anything, `Apply All` and `Apply with Validate` run a dry-run and show a confirmation dialog summarizing additions, changes, and — whenever `CleanupStaleEntries` would remove any entries — how many, giving you a chance to cancel or inspect the details first.

## CI Integration

The following methods can be invoked via `-executeMethod`:

- `AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI`
- `AddressTeller.Editor.AddressTellerMenu.ApplyWithValidateCLI` (runs Validate first and aborts Apply if any issues are found)
- `AddressTeller.Editor.AddressTellerMenu.CheckCLI` (dry-run without Apply — detects drift and issues in read-only mode)
- `AddressTeller.Editor.AddressTellerMenu.ClearCLI` (removes entries from AddressTeller-managed groups by default; pass `-addressTellerClearScope all` to remove all Addressable entries in the project instead; requires `-addressTellerConfirmClear`)

Specifying `-addressTellerReport <path>` / `-addressTellerReportFormat json|junit` outputs a structured report file: `CheckCLI` reports its dry-run results; `ApplyAllCLI` / `ApplyWithValidateCLI` report the pre-apply diff (dry-run). If `-addressTellerReportFormat` is omitted, the format is `junit` when the extension is `.xml`, otherwise `json`.

All four CLI entry points check, before doing anything else, whether AddressTeller's settings file can be loaded (see [Project Settings](#project-settings) below). If it cannot, the run logs an error and exits with code 3 instead of proceeding.

`ApplyAllCLI` / `ApplyWithValidateCLI` / `ClearCLI` call `AssetDatabase.SaveAssets()` right before exiting, so persisting a change they made to disk does not depend on `EditorApplication.Exit`'s own asset-flushing behavior.

`CheckCLI` exit codes (read-only; never writes):

| Exit code | Meaning |
|---|---|
| 0 | No drift, no issues |
| 1 | Drift detected (changes present, no Validation errors) |
| 2 | Validation errors present |
| 3 | Environment error (`AddressableAssetSettings` missing, invalid arguments, an unknown rule class name in `-addressTellerDisableRules`, report write failure, a settings load failure, or an unexpected exception) |

`ApplyAllCLI` / `ApplyWithValidateCLI` exit codes (writes on success; the exit code does not depend on whether there was drift — use `CheckCLI` to detect drift without applying):

| Exit code | Meaning |
|---|---|
| 0 | Applied successfully, regardless of whether there was drift |
| 2 | Validation errors present |
| 3 | Environment error (`AddressableAssetSettings` missing, invalid arguments, an unknown rule class name in `-addressTellerDisableRules`, report write failure, a settings load failure, or an unexpected exception) |

`ClearCLI` exit codes:

| Exit code | Meaning |
|---|---|
| 0 | Clear completed |
| 2 | The same asset has an entry in two or more Addressables groups at once (`ValidationStatus.DuplicateAssetEntry`); aborted before saving the pre-clear snapshot |
| 3 | Environment error (`AddressableAssetSettings` missing, invalid arguments, a rule configuration error that makes `ownedGroups` untrustworthy for `scope=managed`, snapshot save failure, a settings load failure, or an unexpected exception) |
| 4 | Rejected because `-addressTellerConfirmClear` was not specified (intentional rejection) |

### Running from the Command Line

None of the four CLI entry points above show a confirmation dialog or otherwise wait for input: each one parses its arguments, runs, logs the result, and always finishes by calling `EditorApplication.Exit(<code>)` itself. This holds even for `ClearCLI`, which performs a destructive operation — in a CLI context it substitutes the missing confirmation dialog with the required `-addressTellerConfirmClear` flag (see the exit code table above) rather than blocking for input. (Contrast this with the equivalent interactive menu items. `Apply All` and `Apply with Validate` run a dry-run first and only show a confirmation dialog when there is anything to apply — if the dry-run finds no changes and no issues, they return without showing a dialog at all (`AddressTellerApplyFlow`). `Clear All Addresses & Labels...` does not dry-run at all and always shows a confirmation dialog, even when there is nothing to remove — it only skips the dialog if a rule configuration error makes managed-group ownership untrustworthy, in which case it aborts before reaching the dialog. See [Apply Methods](#apply-methods) above.)

Example: running `ApplyAllCLI` headless, writing a JSON report, and using the process exit code to decide pass/fail:

```
"<path-to-Unity-executable>" -batchmode -quit -projectPath "<path-to-project>" -executeMethod AddressTeller.Editor.AddressTellerMenu.ApplyAllCLI -addressTellerReport report.json -logFile -
```

- `-batchmode` runs Unity headless. `-quit` is unnecessary in the normal case, since each CLI method above already calls `EditorApplication.Exit(<code>)` itself before returning — but it is worth keeping as a safety net in case an unexpected exception (one this package's own error handling does not catch, e.g. `ApplyAllCLI`'s call into `AddressTellerService.ApplyAll`) prevents that call from being reached.
- `-logFile -` streams the Editor log to stdout instead of a file, which is useful for capturing this package's `Debug.Log` / `Debug.LogError` output in CI.
- Swap in `CheckCLI`, `ApplyWithValidateCLI`, or `ClearCLI` (which additionally requires `-addressTellerConfirmClear`, and optionally `-addressTellerClearScope all`) as needed.
- Inspect the exit code against the tables above to decide whether the run should fail the build.

### Logical Bundle Distribution Summary

The `json` report includes a `BundleDistribution` section (JSON keys mirror the C# field names verbatim, since `JsonUtility` does not apply any casing convention). This is an estimate of logical bundle units computed from the dry-run Predict results (asset → group/labels) and each group's BundleMode (PackTogether/PackSeparately/PackTogetherByLabel). It is intended as a quick check for unintended extreme distributions (e.g., one huge bundle or hundreds of tiny ones), and **does not guarantee accuracy against an actual Addressables build**.

Known approximation differences:

- PackTogether's scene-level bundle splitting
- PackSeparately's per-folder grouping
- PackTogetherByLabel label combination differences (this summary uses sorted label sets joined with a separator as a normalization key)

Groups without `BundledAssetGroupSchema` cannot have their BundleMode determined and are treated as `Unknown`; they are excluded from `TotalLogicalBundleCount` and counted separately in `UnknownGroupCount`. See [Compatibility Policy](compatibility.md#5-report-output-json--junit-xml) for the full list of JSON keys and their stability guarantees.

## Project Settings

Under `Project Settings > AddressTeller`:

| Setting | Default | Summary |
|---|---|---|
| Auto-apply on import | OFF | Runs the equivalent of `Apply All` automatically via `AssetPostprocessor` on import/move/delete. Recommended as step 2 of adopting AddressTeller, once you're satisfied with what a manual `Apply All` does (see [Quick Start](../README.md#quick-start)). |
| Postprocessor execution order | `1000` | Passed to `AssetPostprocessor.GetPostprocessOrder()`. Lower values run earlier. |
| **Remove unmatched entries** | **OFF** | Deletes owned-group entries no rule matches anymore — can delete manually registered entries too (see below). Recommended as step 3, once you know which groups your rules own; turn it on and immediately run `Apply All` from the menu afterward so the resulting deletions go through the confirmation dialog and an automatic snapshot. While it's off, an entry that would be removed is instead reported once as `ValidationStatus.UnmatchedEntryKept` (a non-blocking notice) by `Validate` / `Apply with Validate` / `CheckCLI` / the Preview windows, so you can see the effect before opting in. |
| Auto-create missing groups | OFF | Creates a group referenced by a rule but missing from Addressables, instead of reporting an error. |
| Snapshot folder | `AddressTellerSnapshots` | Where snapshots are saved, relative to the project root. |
| Auto-snapshot before Apply | ON | Saves a safety snapshot before the `Apply All` / `Apply with Validate` menu items. |
| Auto-snapshot retention count | `10` | Auto-snapshots beyond this count are deleted automatically (minimum 1). |
| Rule enable/disable (per rule class) | all enabled | Excludes a rule class from `Apply All` / `Validate` / `Apply with Validate` / `Explain` / dry-run predictions. |

Details on the settings above that need more than a one-line summary:

- **Postprocessor execution order** (`PostprocessOrder`) — The high default puts AddressTeller after other packages' postprocessors, so assets generated or modified by those run first.
- **Remove unmatched entries** (`CleanupStaleEntries`) — On apply (a manual `Apply All`, and the automatic apply on import for the changed assets), removes entries no longer matched by any rule from groups AddressTeller owns. See [Design Decisions: Deletions Are Determined by Per-Asset Ownership](design-decisions.md#deletions-are-determined-by-per-asset-ownership) for what "owns" means and what this implies for manually registered entries. When enabled, this also removes entries in owned groups whose asset path is structurally invalid for an Addressables entry (not just entries no longer matched by any rule) — for example leftovers created by an older AddressTeller version. Entries whose path cannot currently be resolved at all (empty `AssetPath`, e.g. an unfetched LFS pointer, an in-progress branch switch, or a missing package) are left alone by this check; a genuinely deleted asset is instead handled by the separate deletion-notification path. This check runs against every entry currently sitting in an owned group, independent of which assets were actually imported/changed, so it also catches leftovers that an incremental Apply (e.g. the auto-apply-on-import Postprocessor) would otherwise never revisit — every Postprocessor run re-scans every entry in every owned group for this, not just the imported/changed assets. While this setting is off, `Validate` / `Apply with Validate` / `CheckCLI` and the Preview windows report each entry that would have been removed as a non-blocking `ValidationStatus.UnmatchedEntryKept` notice instead of deleting it — the Console logs a single count line rather than one line per entry (the full list is always in the Result Window and in a JSON/JUnit report); the incremental apply from `AddressTellerPostprocessor` does not report this notice at all. See also [Design Decisions: Missing Groups Are an Error](design-decisions.md#missing-groups-are-an-error-default).
- **Auto-create missing groups** (`AutoCreateMissingGroups`) — When a group referenced by a rule does not exist, Apply will create it by duplicating the DefaultGroup schema. Validate/Predict only displays it as a pending creation and does not actually create the group. Creation can still fail (`DefaultGroup` unavailable, or `AddressableAssetSettings.CreateGroup` throwing) — in that case Apply reports `ValidationStatus.GroupCreationFailed` for the affected assets and does not write to them. Every successful auto-creation, and every creation failure, logs its own `Debug.LogWarning` (one line per group). See also [Design Decisions: Missing Groups Are an Error (Default)](design-decisions.md#missing-groups-are-an-error-default).

These settings are saved to `ProjectSettings/AddressTellerSettings.json`, which can be version-controlled and shared across team members. If the file does not exist yet, AddressTeller runs on default values without creating one; the file is only created the first time a setting is actually changed to a non-default value. Each property setter writes this file on change (assigning the value a property already has is a no-op and does not write); the write reloads the file first if it changed on disk since it was last read (for example after a `git pull`), so a concurrent edit from another source is not silently discarded. If the settings file cannot be loaded — it exists but cannot be read, or is not recognized as an AddressTeller settings file — AddressTeller logs an error and refuses to run `Apply All` / `Validate` / `Apply with Validate` / Preview / Explain / the CLI entry points until the file is fixed or replaced; every setter also throws in this state instead of writing on top of the broken file. The Project Settings page itself still opens so the file can be fixed from there: all fields are disabled while the file is unreadable, and a "Back Up Broken File (.bak) and Recreate with Defaults" button appears (after a confirmation dialog, since it resets every setting — including "Auto-apply on import" and "Remove unmatched entries" — to defaults) to move the broken file aside and start over with a fresh, valid file at default values. The backup file name includes a timestamp, so backing up more than once does not overwrite an earlier backup; these `.bak` files are a local safety copy and are not meant to be committed to version control. When no settings file exists yet, the Project Settings page shows a one-line notice that the displayed values are defaults and that changing any of them will create the file. `ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI` / `ClearCLI` log the same "no settings file" notice (as an Info line) when run without a settings file present; the automatic apply on import (`AssetPostprocessor`) does not, to avoid logging on every import in projects that intentionally run on defaults.

The same screen shows the list of registered rule classes (`AddressRuleBase` subclasses) and their `Order` values. Each class has an enable/disable toggle for temporarily disabling specific rules during debugging or verification. Disabled rules are excluded from `Apply All` / `Validate` / `Apply with Validate` / `Explain` / snapshot dry-run predictions. Each rule row also has a "Validate/Apply this rule only" button that runs a dry-run of that single rule against every asset in the project and opens the result window; because only one rule is in scope, entry-removal predictions for entries owned by other rules are not shown (a notice to that effect appears in the result window). Conversely, if another rule also targets the same group, assets that only that other rule matches can appear as "Removed" in this single-rule preview even though `Apply All` would not remove them (since `Apply All` evaluates every rule together) — run `Apply All` or `Validate` afterward to see the final cross-rule result. This button ignores the enable/disable toggle: a disabled rule can still be dry-run individually by clicking it explicitly.

Note: stale-entry cleanup on asset deletion (`CleanupStaleEntries`, etc.) always considers all rules regardless of the enabled/disabled toggle. This ensures that entries previously managed by a now-disabled rule are still correctly tracked so orphaned entries do not accumulate.

## Snapshots

Via the `Tools/AddressTeller/Snapshot/` menu, you can save, restore, and compare the current Addressables state (groups, addresses, labels) as JSON.

- **Save Snapshot**: Saves the current state to a JSON file.
- **Restore Snapshot (Additive)**: Writes the snapshot contents back. Labels not in the snapshot are left as-is.
- **Restore Snapshot (Exact)**: Writes the snapshot contents back and strips any labels not in the snapshot to achieve an exact match.
- **Compare with Current State / Compare Two Snapshots**: Outputs additions, deletions, and changes to the Console.

The save folder can be changed in Project Settings (default: `AddressTellerSnapshots/` in the project root, outside Assets). It is a local safety net rather than shared project data, so committing it to version control is usually unnecessary — add it to `.gitignore` (or your VCS's equivalent). If your team wants to share specific snapshots (e.g. a known-good state before a risky rule change), commit those particular files deliberately instead of the whole folder.

### Auto Safety Snapshot

When `Tools/AddressTeller/Apply All` or `Tools/AddressTeller/Apply with Validate` is run from the menu, the current state is automatically saved as a snapshot and managed with rotation. `Tools/AddressTeller/Undo Last Apply` restores from the latest auto-snapshot in Exact mode, allowing safe recovery from changes like `CleanupStaleEntries` deletions.

Configurable in Project Settings:

- **Auto-snapshot before Apply** (default: ON) — When off, no auto-save occurs on menu execution.
- **Auto-snapshot retention count** (default: 10, minimum: 1) — Auto-snapshots older than the specified count are automatically deleted.

The auto-snapshot feature applies only to the `Tools/AddressTeller/Apply All` and `Tools/AddressTeller/Apply with Validate` menu items. It does not apply to import-time auto-apply or CLI (`ApplyAllCLI`/`ApplyWithValidateCLI`).

## Troubleshooting

**An asset ends up registered in two Addressables groups at once** (`ValidationStatus.DuplicateAssetEntry`). This can happen after a merge combines two branches that each added the same asset to a different group — it is ambiguous which of the existing duplicate entries is the asset's entry (its address, labels, and any manual edits), and which one survives or gets looked up is decided independently of the rules (for example by the order of the groups). Addressables may drop such a duplicate on its own when a group asset is imported, but not always. When this happens, every entry point that would otherwise write, remove, or restore anything (`Apply All`, `Validate`, `Apply with Validate`, `CheckCLI`, `ApplyAllCLI`, `ApplyWithValidateCLI`, the import-time auto-apply, `Undo Last Apply`, `Clear All Addresses & Labels...`/`ClearCLI`, and Snapshot Restore from the Snapshot Manager window) reports it and stops without touching *any* asset that run (not just the duplicated one), since AddressTeller has no way to know which of the duplicate entries is the one to keep. The message names the asset and every group/address it currently sits in.

To fix it: before removing anything, note down the labels and address on each of the duplicate entries (labels not assigned by a rule won't come back on their own once the entries are gone). Then open the **Addressables Groups** window and select the asset's entry in *every* group named in the message, then remove all of them (right-click → **Remove Addressables**, confirming the dialog that follows) — do not try to keep one by removing only the others, since the window's own removal also just deletes whichever entry it happens to find first for that GUID, not necessarily the one you right-clicked. Once none of the duplicate entries remain, either add the asset back to the group it should belong to yourself, or run `Apply All` and let your rules recreate the entry. Afterward, check the Groups window to confirm the asset landed in the group you expect before relying on it again.

## Samples

The following samples can be imported from the Package Manager's Samples tab (`Samples~/`):

- **Basic Rules** — Minimal rule definition example.
- **Folder-based Rules** — Example that maps folder hierarchy directly to addresses and labels.
- **Type-based Rules** — Example that routes assets to groups and labels by asset type.
- **Rule Unit Test Helper** — Helper and NUnit sample for unit-testing `AddressRuleBase` subclasses without a live Addressables project.
