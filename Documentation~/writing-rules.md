[日本語](./writing-rules.ja.md)

# Writing Rules

## AddressRuleBase

```csharp
public abstract class AddressRuleBase
{
    public virtual int Order => 0;          // Evaluation order and address priority — lower wins
    public abstract void Configure(IAddressRuleBuilder rules);
}
```

Classes that inherit `AddressRuleBase` are collected automatically from assemblies — no central registration required. Place them under an `Editor` folder.

If you define rule classes in a custom assembly definition (asmdef), add `AddressTeller.Core` to its `references`. Everything a rule class touches — `AddressRuleBase`, `IAddressRuleBuilder`, `Match`, `AssetCondition`, `Naming`, `AssetContext`, and the `AddressTeller.Testing.RuleInspector` used for unit tests — lives in that assembly, so a rule-only assembly needs nothing else. (The `Rule Unit Test Helper` sample's asmdef is exactly this case: it references `AddressTeller.Core` alone.)

Add `AddressTeller.Editor` on top of that only if the same assembly also calls the operational APIs — `AddressTellerService`, `ValidationResult`, `AddressTellerSnapshotService`, the report types, and so on. Those live in `AddressTeller.Editor` but expose Core types in their signatures (e.g., `ValidationResult.Context` is `AddressTeller.AssetContext` from Core), so referencing `AddressTeller.Editor` always requires referencing `AddressTeller.Core` as well.

## Group / Where / Address / Label

```csharp
rules.Group("GroupName")
    .Where(ctx => /* bool */)                 // One call per group only. Combine multiple conditions with &&
    .Address(ctx => /* string */)             // Or .Address("fixed string")
    .Label(ctx => /* string */)               // Can be called multiple times. .Label("fixed string") also works
    .Label("another label");
```

- `Where(predicate)` / `Where(predicate, description)` can be called **only once per group**. A second call throws `InvalidOperationException`.
  Providing a `description` includes it in conflict error messages.
- If `Address()` is not called, the group rule emits no address (useful for label-only rules).
- `Label()` can be called **any number of times**. Labels accumulate from all matching rules.
- `Group()` can be called multiple times within the same `Configure()` to define multiple rule entries. Note that `Order` is per rule *class*, not per `Group()` call — several `Group()` calls in the same `Configure()` all share the same `Order` and therefore tie if they ever address the same asset (see [Address priority and conflicts](#evaluation-rules-and-behavior)). To let one rule override another by priority, put them in separate classes with different `Order` values.

## GroupDefault

Use `GroupDefault()` instead of `Group("name")` to assign addresses and labels to the Addressables DefaultGroup.
`Where`/`Address`/`Label` chain exactly as with `Group()`.

```csharp
rules.GroupDefault()
    .Where(ctx => ctx.IsInFolder("Assets/Game/Misc"))
    .Address(ctx => ctx.FileNameWithoutExtension);
```

- DefaultGroup is resolved from `AddressableAssetSettings.DefaultGroup` at evaluation time, so **it follows DefaultGroup renames automatically** (no need to hard-code the group name).
- If `Group("actual name")` and `GroupDefault()` resolve to the same physical group, conflict detection still applies as usual.

## AnyGroup

Use `AnyGroup()` for a label-only rule that doesn't target a specific group — the builder it returns has no `Address()`.

```csharp
rules.AnyGroup()
    .Where(ctx => ctx.Path.Contains("/Characters/"))
    .Label("character");
```

- `AnyGroup()` never assigns an address and never creates or moves an entry into any group — it only adds labels to an asset's existing entry, in whichever group that entry currently belongs to. It does not make AddressTeller own that group (see [Design Decisions: Deletions Are Determined by Per-Asset Ownership](design-decisions.md#deletions-are-determined-by-per-asset-ownership)).
- `Where`/`Label`/`IncludeFolders` chain the same way as with `Group()`.

## Match / Naming Helpers

Helper classes let you express common condition predicates and address generation patterns concisely.

### Match Static Class

Builds common conditions. Auto-generated descriptions appear in the Explain window and in rule validation error messages.

```csharp
using AddressTeller;

rules.Group("Characters")
    .Where(Match.InFolder("Assets/Game/Characters")
               .And(Match.OfType<GameObject>()))
    .Address(Naming.FileNameWithoutExtension())
    .Label("character");
```

Key methods:
- `Match.InFolder(string path)` — matches assets under the specified folder
- `Match.OfType<T>()` — matches assets of the specified type (GameObject, Sprite, etc.)
- `Match.Glob(string pattern)` — matches by wildcard pattern against the full path (e.g., `Assets/**/*.prefab`; a bare `*.prefab` never matches anything, since `*` doesn't cross `/` and there is no asset path without one — see the XML doc on `Match.Glob` for the full syntax)
- `Match.All()` — always true (unconditional rule)
- `condition.And(otherCondition)` — combines conditions with AND

If your rule file also uses `System.Text.RegularExpressions`, add `using Match = AddressTeller.Match;` to disambiguate the two `Match` types.

### Naming Static Class

Common patterns for generating address values. Handles path normalization details so you don't have to.

```csharp
.Address(Naming.FileNameWithoutExtension())
.Address(Naming.ParentFolderName())
.Address(Naming.RelativePath("Assets/Game"))
```

Key methods:
- `Naming.FileName()` — file name with extension
- `Naming.FileNameWithoutExtension()` — file name without extension
- `Naming.ParentFolderName()` — parent folder name
- `Naming.RelativePath(string root)` — relative path from the specified folder

## AssetContext

Per-asset information passed to each rule.

| Property | Description | Example |
|---|---|---|
| `Guid` | Asset GUID | |
| `Path` | Path from `Assets/` (normalized to `/` separators) | `"Assets/Game/Characters/Player.prefab"` |
| `Type` | Asset type | `typeof(GameObject)` |
| `FileNameWithoutExtension` | File name without extension | `"Player"` |
| `FileName` | File name with extension | `"Player.prefab"` |
| `Directory` | Directory path | `"Assets/Game/Characters"` |
| `Extension` | File extension | `".prefab"` |
| `IsInFolder(string)` | Returns true if the asset is under the specified folder | `ctx.IsInFolder("Assets/Game")` → `true` |
| `IsFolder` | True when this asset is a folder rather than a file | `true` for `"Assets/Game/Characters"` |
| `PathSegments` | Path split by `/` | `["Assets", "Game", "Characters", "Player.prefab"]` |
| `RelativePathFrom(string root)` | Relative path from the specified folder | `ctx.RelativePathFrom("Assets/Game")` → `"Characters/Player.prefab"` |

## Assets Excluded from Evaluation

Some paths never reach any rule's `Where()` at all — not because a rule excluded them, but because AddressTeller pre-filters anything Addressables itself would refuse to register as an entry (the same path-validity check the Groups window's drag-and-drop and the Inspector's Addressable checkbox apply; the Inspector checkbox additionally rejects assets whose main type belongs to an editor assembly, a check AddressTeller does not replicate here). This keeps AddressTeller from creating entries that couldn't have been created by hand.

Excluded regardless of `IncludeFolders()`:
- Anything outside `Assets/` and outside a package's own folder (e.g. files under `ProjectSettings/` or `Library/`), and a package's own `package.json`.
- Files with one of these extensions: `.cs`, `.js`, `.boo`, `.exe`, `.dll`, `.meta`, `.preset`, `.asmdef`.
- Anything under a path segment named `Editor` (e.g. `Assets/Game/Editor/Foo.asset`).
- Paths with no extension that are exactly `Assets`, a folder named `Editor` itself, or a path under a folder named `Editor` (whether or not the path is actually a folder — see note below).
- The configured Addressables Config Folder itself and anything under it (the folder holding `AddressableAssetSettings.asset` and related assets), matched by plain prefix like Addressables itself does — so a folder that merely *starts with* the same name (e.g. `Assets/AddressableAssetsData_Backup`) is excluded too, not just the Config Folder's actual contents.

This mirrors Addressables' own internal entry-validity check, which works on the path string alone. Because of that, a path with no extension is treated as folder-like by this check even when it happens to be an ordinary file — for example, a real file literally named `Assets/Game/Editor` with no extension is excluded too. `AssetContext.IsFolder` (used below for the `IncludeFolders()` opt-in) is a separate, actual folder check and plays no part in this exclusion.

## Folders (IncludeFolders)

By default, rules never see folders: `Where()` is never invoked for a folder unless the rule opts in with `IncludeFolders()`. This keeps existing rules (written with files in mind) safe from unintentionally matching a folder through a broad `Where` such as `Match.InFolder(...)`, whose prefix match would otherwise catch subfolders too.

```csharp
rules.Group("Bundles")
    .Where(ctx => ctx.IsFolder && ctx.FileName == "StreamingContent")
    .IncludeFolders()
    .Address(ctx => ctx.FileName);
```

- `IncludeFolders()` can be called once per rule (same one-call limit as `Where()` / `Address()`); calling it a second time throws `InvalidOperationException`.
- Once a rule opts in, `ctx.IsFolder` distinguishes folders from files inside that rule's `Where` / `Address` / `Label` selectors.
- A folder entry, once created by Addressables, implicitly covers every asset beneath it — labels assigned to the folder are inherited by those assets. Matching a folder with one rule and its contents with another rule creates two separately managed entries covering the same assets; make sure that's what you intend.
- Files without an extension (e.g. `LICENSE`) are ordinary files, not folders — `IsFolder` is `false` for them regardless of `IncludeFolders()`. Note that a subset of extension-less paths (the `Assets` root, a folder named `Editor` itself, and paths under a folder named `Editor`) are excluded from evaluation entirely, whether or not they are actually folders — see [Assets Excluded from Evaluation](#assets-excluded-from-evaluation) above.
- Even with `IncludeFolders()` declared, the exclusions listed in [Assets Excluded from Evaluation](#assets-excluded-from-evaluation) still apply — Addressables itself would refuse to register those paths as entries either way.

## Testing

Unit-test `AddressRuleBase` subclasses in isolation using the public `RuleInspector` API (namespace `AddressTeller.Testing`, requires reference to `AddressTeller.Core` assembly) and the `Rule Unit Test Helper` sample. `RuleInspector.Collect()` retrieves the rule entries defined by `Configure()` without a live Addressables project; you supply test fixtures and verify rule behavior. For detailed usage examples and NUnit integration patterns, import **Rule Unit Test Helper** from the Package Manager's Samples tab and refer to the bundled README.

## Evaluation Rules and Behavior

- **Evaluation order**: All rules are evaluated in ascending `Order`. A warning is issued during `Apply All` / `Validate` when multiple rule classes share the same `Order` value, since two rules sharing an `Order` will conflict if they ever both produce an address for the same asset (see below).
- **Address priority and conflicts**: `Order` doubles as a priority. When two or more matching rules call `Address()` for the same asset, the one with the lowest `Order` wins and its address is written. This is the supported way to let a narrow rule override a broad one:

  ```csharp
  public class DefaultAudioRule : AddressRuleBase
  {
      public override int Order => 100; // broad, low priority

      public override void Configure(IAddressRuleBuilder rules)
      {
          rules.Group("Audio")
              .Where(ctx => ctx.IsInFolder("Assets/Audio"))
              .Address(ctx => ctx.FileNameWithoutExtension);
      }
  }

  public class BossAudioRule : AddressRuleBase
  {
      public override int Order => 0; // narrow, high priority — wins over DefaultAudioRule

      public override void Configure(IAddressRuleBuilder rules)
      {
          rules.Group("Audio/Boss") // create this group first — AutoCreateMissingGroups is OFF by default
              .Where(ctx => ctx.IsInFolder("Assets/Audio/Boss"))
              .Address(ctx => $"boss/{ctx.FileNameWithoutExtension}");
      }
  }
  ```

  A **conflict error** occurs only when two or more of the *lowest-`Order`* matching rules tie — in that case no write is performed for that asset (applies to both Apply and Validate). See [Design Decisions: Address Priority and Conflicts](design-decisions.md#address-priority-and-conflicts) for the rationale.
- **Duplicate addresses across assets**: The conflict above is about one asset with multiple rule candidates; it says nothing about two *different* assets ending up with the same address string — for example, two rules both calling `Naming.FileNameWithoutExtension()` on files that happen to share a name in different folders. `Validate` / `Apply All` separately scan for this and report it as `ValidationStatus.DuplicateAddress`; it never blocks a write, but it is reported as an error when AddressTeller itself would write one of the colliding addresses this run. See [Design Decisions: Address Priority and Conflicts](design-decisions.md#address-priority-and-conflicts) for how this differs from the per-asset conflict above.
- **Label accumulation**: `Label()` accumulates from all matching rules regardless of mode (multiple labels are assigned simultaneously). See [Design Decisions: Labels Accumulate from All Rules](design-decisions.md#labels-accumulate-from-all-rules).
- **No matching rule**: The asset is skipped. If `CleanupStaleEntries` is enabled (see [Apply & Operations](operations.md)), any existing entries in AddressTeller-managed groups are removed. This removal only applies when *no* rule matches at all (neither an address nor a label). If a label-only rule (e.g. `AnyGroup()` or a `Group()` rule with no `Address()`) still matches, the entry is **not** removed, and its labels are updated on the existing entry instead. In this label-only case, the existing entry's address and group are left unchanged — they keep whatever value was assigned the last time an address rule matched for that asset. Only its labels are updated, and this happens regardless of which group the existing entry belongs to — including a group no rule declares `Address()` for (label writes are additive and are not gated by ownership; see [Design Decisions: Deletions Are Determined by Per-Asset Ownership](design-decisions.md#deletions-are-determined-by-per-asset-ownership)). If the asset has no existing entry at all, nothing is created — a label-only rule never creates an entry by itself.
- **Group not found**: Results in a `GroupNotFound` error. Groups are not created automatically — create them first in the Addressable Groups window. The opt-in setting **Auto-create missing groups** (`AutoCreateMissingGroups`, default: OFF) makes Apply create the group instead; see [Project Settings](operations.md#project-settings). See also [Design Decisions: Missing Groups Are an Error](design-decisions.md#missing-groups-are-an-error-default).
- **Exception inside a rule**: Only that rule is reported as `RuleError`; processing continues for other rules and other assets. If the failing rule was the lowest-`Order` (highest-priority) match for an asset, its candidate is simply absent from evaluation — a lower-priority rule's address is adopted instead, without any conflict being reported for that asset.
