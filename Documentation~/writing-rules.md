[日本語](./writing-rules.ja.md)

# Writing Rules

## AddressRuleBase

```csharp
public abstract class AddressRuleBase
{
    public virtual int Order => 0;          // Evaluation order — lower values are evaluated first
    public abstract void Configure(IAddressRuleBuilder rules);
}
```

Classes that inherit `AddressRuleBase` are collected automatically from assemblies — no central registration required. Place them under an `Editor` folder.

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
- `Group()` can be called multiple times within the same `Configure()` to define multiple rule entries.

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
- `Match.Glob(string pattern)` — matches by wildcard pattern (e.g., `*.prefab`)
- `Match.All()` — always true (unconditional rule)
- `condition.And(otherCondition)` — combines conditions with AND

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
| `PathSegments` | Path split by `/` | `["Assets", "Game", "Characters", "Player.prefab"]` |
| `RelativePathFrom(string root)` | Relative path from the specified folder | `ctx.RelativePathFrom("Assets/Game")` → `"Characters/Player.prefab"` |

## Evaluation Rules and Behavior

- **Evaluation order**: All rules are evaluated in ascending `Order`. A warning is issued during `Apply All` / `Validate` when multiple rule classes share the same `Order` value.
- **Address conflicts**: If two or more matching rules call `Address()`, a **conflict error** occurs and no write is performed for that asset (applies to both Apply and Validate). Only a single matching rule's address is accepted. See [Design Decisions: Address Conflicts Cause an Error](design-decisions.md#address-conflicts-cause-an-error) for the rationale.
- **Label accumulation**: `Label()` accumulates from all matching rules regardless of mode (multiple labels are assigned simultaneously). See [Design Decisions: Labels Accumulate from All Rules](design-decisions.md#labels-accumulate-from-all-rules).
- **No matching rule**: The asset is skipped. If `CleanupStaleEntries` is enabled (see [Apply & Operations](operations.md)), any existing entries in AddressTeller-managed groups are removed.
- **Group not found**: Results in a `GroupNotFound` error. Groups are not created automatically — create them first in the Addressable Groups window. See [Design Decisions: Missing Groups Are an Error](design-decisions.md#missing-groups-are-an-error-default).
- **Exception inside a rule**: Only that rule is reported as `RuleError`; processing continues for other rules and other assets.
