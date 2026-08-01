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

Exit codes (`ApplyAllCLI` / `ApplyWithValidateCLI` / `CheckCLI`):

| Exit code | Meaning |
|---|---|
| 0 | No drift, no issues |
| 1 | Drift detected (changes present, no Validation errors) |
| 2 | Validation errors present |
| 3 | Environment error (`AddressableAssetSettings` missing, invalid arguments, an unknown rule class name in `-addressTellerDisableRules`, or report write failure) |

`ClearCLI` exit codes:

| Exit code | Meaning |
|---|---|
| 0 | Clear completed |
| 3 | Environment error (`AddressableAssetSettings` missing, invalid arguments, a rule configuration error that makes `managedGroups` untrustworthy for `scope=managed`, or snapshot save failure) |
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

### Logical Bundle Distribution Summary

The `json` report includes a `BundleDistribution` section (JSON keys mirror the C# field names verbatim, since `JsonUtility` does not apply any casing convention). This is an estimate of logical bundle units computed from the dry-run Predict results (asset → group/labels) and each group's BundleMode (PackTogether/PackSeparately/PackTogetherByLabel). It is intended as a quick check for unintended extreme distributions (e.g., one huge bundle or hundreds of tiny ones), and **does not guarantee accuracy against an actual Addressables build**.

Known approximation differences:

- PackTogether's scene-level bundle splitting
- PackSeparately's per-folder grouping
- PackTogetherByLabel label combination differences (this summary uses sorted label sets joined with a separator as a normalization key)

Groups without `BundledAssetGroupSchema` cannot have their BundleMode determined and are treated as `Unknown`; they are excluded from `TotalLogicalBundleCount` and counted separately in `UnknownGroupCount`. See [Compatibility Policy](compatibility.md#5-report-output-json--junit-xml) for the full list of JSON keys and their stability guarantees.

## Project Settings

Under `Project Settings > AddressTeller`:

- **Auto-apply on import** (default: ON) — When off, `AssetPostprocessor` auto-apply is disabled. Manual menu operations are unaffected.
- **Postprocessor execution order** (`PostprocessOrder`, default: 1000) — Passed to `AssetPostprocessor.GetPostprocessOrder()`. Lower values run before other `AssetPostprocessor`s. The high default puts AddressTeller after other packages' postprocessors, so assets generated or modified by those run first. Note: `0` is reserved as the "unset" sentinel — explicitly setting the field to `0` is treated the same as leaving it unset and falls back to `1000`.
- **Remove unmatched entries** (`CleanupStaleEntries`, default: ON) — During `Apply All`, removes assets from AddressTeller-managed groups (groups referenced by at least one rule) that no longer match any rule. Deletion is per-entry (`RemoveAssetEntry`), removing both the address and Addressables labels. Entries in groups AddressTeller does not manage are never touched. **However, manually registered entries inside a managed group will be deleted if no rule matches them** (only per-asset matching is checked). See [Design Decisions: Deletions Are Determined by Per-Asset Ownership](design-decisions.md#deletions-are-determined-by-per-asset-ownership) and [Design Decisions: Missing Groups Are an Error](design-decisions.md#missing-groups-are-an-error-default).
- **Snapshot folder** (see below)

These settings are saved to `ProjectSettings/AddressTellerSettings.asset`, which can be version-controlled and shared across team members.

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
