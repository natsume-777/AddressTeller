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
    .Where(Match.InFolder("Assets/X"))        // One call per group only. Prefer Match.* helpers; combine with And / Or / Not (a raw lambda ctx => bool also works)
    .Address(ctx => /* string */)             // Or .Address("fixed string")
    .Label(ctx => /* string */)               // Can be called multiple times. .Label("fixed string") also works
    .Label("another label");
```

- `Where(predicate)` / `Where(predicate, description)` / `Where(condition)` / `Where(condition, description)` can be called **only once per group**, counting all four overloads together. A second call throws `InvalidOperationException`.
  Providing a `description` includes it in conflict error messages. Prefer building the condition from [`Match.*` helpers](#match-static-class), which describe themselves; write a `description` by hand only for a raw lambda that the helpers cannot express.
- To give a `Match.*` condition your own description, call `Where(condition, description)`. It keeps everything the condition declares (folders, exclusions), so the missing-folder check and the unmatched-asset listing still work, and the `description` replaces the generated one in messages (`null` means no description). Do not write `Where(Match.InFolder(...).Predicate, "text")` or pass the condition where a `Func<AssetContext, bool>` is expected: that converts it to a bare predicate and drops its declarations. The same applies when combining: use `a.And(b, "text")` and `new AssetCondition(b, "text")` with a condition `b` (both keep its declarations, including a `Not(InFolder ...)` exclusion); a lambda `b` still goes through the predicate overloads. The description means different things in the two places: `Where(condition, "text")` replaces the description of the whole rule, while `a.And(b, "text")` replaces only `b`'s description and joins it to `a`'s (`A AND text`), the same as `a.And(lambda, "text")`; to describe a combination as a whole, write `Where(a.And(b), "text")`.
- If `Address()` is not called, the group rule emits no address (useful for label-only rules).
- `Label()` can be called **any number of times**. A rule's labels are written only when that rule's address wins for the asset (see [Address priority and conflicts](#evaluation-rules-and-behavior)); if its address loses on `Order`, none of its labels are written. A rule with no `Address()` (a label-only rule) is not competing for an address, so its labels are always written when it matches. To attach a label regardless of which address rule wins, put it in a separate [`AnyGroup()`](#anygroup) rule.
- `Group()` can be called multiple times within the same `Configure()` to define multiple rule entries. Note that `Order` is per rule *class*, not per `Group()` call — several `Group()` calls in the same `Configure()` all share the same `Order` and therefore tie if they ever address the same asset (see [Address priority and conflicts](#evaluation-rules-and-behavior)). To let one rule override another by priority, put them in separate classes with different `Order` values.
- `Group(groupName)` throws `ArgumentException` if `groupName` contains `/` or `\`. Addressables itself replaces those characters with `-` when it creates or renames a group, so a rule referencing the un-replaced name would never match the group Addressables ends up with — use the name Addressables ends up with instead. This exception is thrown by `Configure()` itself and is handled the same way as any other exception inside `Configure()` — see [Exception inside `Configure()`](#evaluation-rules-and-behavior).

## GroupDefault

Use `GroupDefault()` instead of `Group("name")` to assign addresses and labels to the Addressables DefaultGroup.
`Where`/`Address`/`Label` chain exactly as with `Group()`.

```csharp
rules.GroupDefault()
    .Where(Match.InFolder("Assets/Game/Misc"))
    .Address(ctx => ctx.FileNameWithoutExtension);
```

- DefaultGroup is resolved from `AddressableAssetSettings.DefaultGroup` at evaluation time, so **it follows DefaultGroup renames automatically** (no need to hard-code the group name).
- If `Group("actual name")` and `GroupDefault()` resolve to the same physical group, conflict detection still applies as usual.

## AnyGroup

Use `AnyGroup()` for a label-only rule that doesn't target a specific group — the builder it returns has no `Address()`.

```csharp
rules.AnyGroup()
    .Where(Match.Glob("Assets/**/Characters/**"))
    .Label("character");
```

- `AnyGroup()` never assigns an address and never creates or moves an entry into any group — it only adds labels to an asset's existing entry, in whichever group that entry currently belongs to. It does not make AddressTeller own that group (see [Design Decisions: Deletions Are Determined by Per-Asset Ownership](design-decisions.md#deletions-are-determined-by-per-asset-ownership)).
- `Where`/`Label`/`IncludeFolders` chain the same way as with `Group()`.
- Its labels are written whenever it matches, no matter which address rule wins for the asset. This is how to attach a label that must stay on an asset even when a narrower, higher-priority address rule takes over the address — for example, a broad `Order = 100` rule that addresses and labels every prop, overridden for one prop by a narrow `Order = 0` rule: the prop gets the narrow rule's address and labels (not the broad rule's), plus every matching `AnyGroup()` label.
- If the asset's existing entry (or its group) is read-only, `AnyGroup()` silently does nothing for that asset — no label is added and nothing is reported. AddressTeller treats a read-only entry or group as owned by something else and leaves it alone, the same way it leaves alone an asset with no entry at all. This applies to every label-only rule (`AnyGroup()`, and a `Group()` rule with no `Address()`), not just `AnyGroup()`. It does not apply to an address rule (`Group()` with `Address()`), which still moves the entry via `CreateOrMoveEntry` and, as an existing and unrelated side effect, clears `ReadOnly` when it does.

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
- `Match.InFolder(string path)` — matches assets under the specified folder (declares the folder; see [Declared Folders](#declared-folders))
- `Match.InFolderByGuid(string guid, bool recursive = true)` — like `InFolder`, but identifies the folder by its asset GUID, so the rule keeps working after the folder is moved or renamed
- `Match.OfType<T>()` — matches assets of the specified type (GameObject, Sprite, etc.)
- `Match.Glob(string pattern)` — matches by wildcard pattern against the full path (e.g., `Assets/**/*.prefab`; a bare `*.prefab` never matches anything, since `*` doesn't cross `/` and there is no asset path without one — see the XML doc on `Match.Glob` for the full syntax)
- `Match.WithExtension(params string[] extensions)` — matches files whose extension is any of the given ones. `"png"`, `".png"` and `".PNG"` are the same; only the last extension of a file name counts, so `".tar.gz"` is rejected with an `ArgumentException` rather than silently never matching; so are wildcards (`*`, `?`), white space inside the extension and characters that cannot appear in a file name. Giving no extension, or a null / empty one, also throws
- `Match.All()` — always true (unconditional rule)
- `Match.Any(params AssetCondition[] conditions)` — matches when any of the conditions matches (OR). Unlike `All()`, giving no condition throws `ArgumentException` (an empty OR would match nothing, which is easy to write by accident); a null element throws `ArgumentNullException`
- `condition.And(otherCondition)` — combines conditions with AND
- `condition.Or(otherCondition)` — combines conditions with OR
- `condition.Not()` — inverts the condition

Descriptions are composed automatically: `Match.InFolder("Assets/A").And(Match.WithExtension("png", "jpg"))` is described as `InFolder(Assets/A) AND WithExtension(.png, .jpg)`, an OR as `(A OR B)`, a NOT as `NOT A`. An `A AND B` inside an OR or a NOT is wrapped in its own parentheses (`((A AND B) OR C)`, `NOT (A AND B)`), so the nesting is never ambiguous; a description you wrote by hand that contains ` AND ` or ` OR ` is wrapped the same way. An OR or a NOT that includes an operand without a complete description (a raw lambda, or an AND that contains one) has no description rather than a misleading partial one. Prefer composing `Match.*` conditions over writing a `description` by hand: the description is then generated from the condition itself, so it cannot drift from what the condition does.

```csharp
// Textures anywhere under Art, except the Raw folder
.Where(Match.InFolder("Assets/Art")
           .And(Match.WithExtension("png", "jpg"))
           .And(Match.InFolder("Assets/Art/Raw").Not()))
```

If your rule file also uses `System.Text.RegularExpressions`, add `using Match = AddressTeller.Match;` to disambiguate the two `Match` types.

### Declared Folders

A condition built with `Match.InFolder`, `Match.InFolderByGuid`, or `Match.Glob` records the folders it depends on (`AssetCondition.Folders`, copied to `AddressRuleEntry.DeclaredFolders` when the condition is passed to `Where()`). `And()`, `Or()`, `Match.All()` and `Match.Any()` take the union of their operands' folders, and `Not()` keeps its operand's folders. Whenever AddressTeller evaluates the rules (Apply, Validate, dry-run), it checks that each declared folder exists:

- A folder that does not exist is reported as `ValidationStatus.DeclaredFolderMissing` (blocking), once per folder, naming the rules that declare it. A rule that depends on a missing folder can never match anything — typically a typo, or a folder that was moved or deleted. (Inside `Not()` it is the other way around: `Match.InFolder("Assets/Missing").Not()` excludes nothing, so a rule built on it can match, and write, every asset that its other conditions accept — including a plain `ApplyAll` and the automatic apply on import, which do not stop on the missing-folder issue.) While any declared folder is missing, AddressTeller also suspends stale entry cleanup for that whole run, the same as when a rule fails to configure: with a rule silently matching nothing, an entry that merely looks unmatched must not be removed. A plain `ApplyAll` call and the automatic apply on import still evaluate and write everything else (other rules, other assets) as usual; but because the issue is blocking, Apply with Validate (the menu item and `ApplyWithValidateCLI`) aborts the whole Apply, `CheckCLI` / `ApplyWithValidateCLI` exit with code 2, and `ApplyAllCLI` writes everything else and then also exits with code 2.
- A folder that exists but differs from the declaration only in letter case (for example `Assets/art` declared, `Assets/Art` on disk) is reported as `ValidationStatus.DeclaredFolderCaseMismatch`, a non-blocking notice. Folder matching ignores case, so the rule still applies; correct the declaration to the real name.
- `Match.Glob` declares the folder made of the whole path segments before its first wildcard: `Assets/Art/*.png` declares `Assets/Art`, and `Assets/Art*/x` declares `Assets`. A pattern that starts with a wildcard (for example `**/foo`) has no fixed prefix and declares nothing, so a missing folder cannot be reported for it. The prefix is only checked for existence; it is never a *scope* of the [Unmatched Assets](operations.md#unmatched-assets-and-rules-that-match-nothing) list, so `Match.Glob("Assets/**/Characters/**")` does not turn all of `Assets` into one (declare a scope with `Match.InFolder`, for example `Match.InFolder("Assets/Characters").And(...)`). This holds under `And()`, `Or()` and `Not()`: a folder that some operand declares with `InFolder` or `InFolderByGuid` is a scope, while a folder declared only by `Glob` prefixes stays check-only, and `Not()` does not turn it into a scope.
- A folder declared inside `Not()` — `Match.InFolder("Assets/Raw").Not()` — is still checked for existence (excluding a folder that does not exist is a silent no-op, which is worth knowing about), but it is not a *scope* of the rule. The [Unmatched Assets](operations.md#unmatched-assets-and-rules-that-match-nothing) list only looks at what a rule covers. For the common form `Match.InFolder("Assets/Art").And(Match.InFolder("Assets/Art/Raw").Not())`, the rule covers `Assets/Art` except what the negated condition matches, so assets under `Assets/Art/Raw` are not listed just because this rule leaves them out. This applies when the `Not` is applied directly to `Match.InFolder`, `Match.InFolderByGuid` or `Match.Glob` (for `Glob`, only the paths the pattern matches are excluded, not its whole fixed prefix) and is then combined with `And()`; the exclusions of several `And()` operands add up. Exclusions belong to the rule that declares them: if another rule covers the same folder, its assets are still listed unless that rule or another one gives them an address or label. A `Not` of any other condition (such as `Or(...).Not()`) only keeps the existence check. `Not(Not(x))` is `x` again. `Or()` and `Match.Any()` keep a scope folder as a scope and drop the exclusions of their operands (they applied to one operand only), so they can list more assets, never fewer. If the same folder is declared both recursively and with `recursive: false`, the recursive one covers it under `Or()`, while `And()` narrows to the direct children.
- A condition built from a raw lambda — `Where(ctx => ctx.IsInFolder("Assets/Foo"))` — declares nothing and is never reported. Use `Match.InFolder` if you want the check.
- `Match.InFolderByGuid` is resolved to the folder's current path each time the rules are evaluated (the lookup result is kept until the start of the next run or the next import, whichever comes first). A GUID that does not resolve to a folder is reported as `DeclaredFolderMissing`, and the condition matches nothing. The GUID is looked up each time the condition is evaluated, so `RuleInspector.Collect` in an Editor unit test sees the same result as AddressTeller's own evaluation; where no GUID lookup is available (outside the Unity Editor's AssetDatabase) the condition never matches. When the same GUID is declared both recursively and with `recursive: false` under `Or()`, the recursive declaration covers it, as with paths; a path declaration and a GUID declaration that resolve to the same folder are not combined this way, so the Unmatched list may include more assets than strictly necessary there.

Between domain reloads, the automatic apply on import (`AddressTellerPostprocessor`) reuses the result of this check and re-checks a folder only when that folder, or a folder above it, was created, deleted, moved, or renamed in the import. Apply All, Validate, and the CLI entry points always check again.

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

## Overlapping Rules (OverriddenBy)

A broad rule that a narrow one overrides on purpose (the `Order` pattern in [Evaluation Rules and Behavior](#evaluation-rules-and-behavior)) and two rules that overlap by accident give the same result: the lower `Order` wins and nothing is reported. AddressTeller lists every pair of rule classes that overlap this way — both match an asset, both produce an address, and one wins on `Order` — with the number of assets and a few example paths, in the `Overlaps` tab of the result window (shown when there is any) and in the `Overlaps[]` list of a `json` report. A tie is a conflict, not an overlap, and neither is an asset whose write is blocked by a rule error; the same goes for a rule that only assigns labels. Only the assets a run examines are counted, so a preview of part of the project lists only the overlaps among those assets.

To say that an override is intended, declare it on the rule whose address is overridden:

```csharp
rules.Group("Audio")
    .Where(ctx => ctx.IsInFolder("Assets/Audio"))
    .Address(ctx => ctx.FileNameWithoutExtension)
    .OverriddenBy<BossAudioRule>();
```

`OverriddenBy<TRule>()` changes neither which rule wins nor what is written; it only marks the pair (this rule class, `TRule`) as expected. The declaration belongs to that `Group()` chain, not to the whole rule class: if a class has two `Group()` chains and only one declares it, the other's overlaps stay undeclared. Call it more than once to allow several rules.

With the **Strict rule overlaps** setting on (`AddressTellerSettings.StrictRuleOverlaps`, default: OFF — see [Apply & Operations](operations.md#project-settings)), each undeclared overlap is reported as a blocking `ValidationStatus.UndeclaredRuleOverlap`, once per pair of rule classes, with the number of assets and a few example paths in the message. Apply with Validate then aborts and `CheckCLI` / `ApplyWithValidateCLI` exit with code 2. The check does not run in `ApplyAll`, so neither the automatic apply on import nor `ApplyAllCLI` enforces it. Which rule wins and what is written never change. To see every rule involved for one of the example paths, use `Assets > AddressTeller > Explain` on it. See [Apply & Operations: Rule Overlaps](operations.md#rule-overlaps).

`OverriddenBy<TRule>()` is only meaningful on a rule that produces an address (a `Group()` chain with `Address()`); pass the concrete rule class. The pair is matched by class name only, so two rule classes with the same name in different namespaces cannot be told apart. A declaration that no longer matches any overlap (for example after the other rule was changed) is not reported.

## Testing

Unit-test `AddressRuleBase` subclasses without a live Addressables project using the public `RuleInspector` API (namespace `AddressTeller.Testing`, requires reference to the `AddressTeller.Core` assembly) and the `Rule Unit Test Helper` sample.

The recommended way is an expectation table: one `RuleExpectation` row per asset, listing what the rules should decide for it, checked with `RuleInspector.Verify`.

```csharp
var failures = RuleInspector.Verify(new AddressRuleBase[] { new CharacterRules() }, new[]
{
    new RuleExpectation("Assets/Characters/Hero.prefab", typeof(GameObject))
        { Group = "Characters", Address = "Hero", Labels = new[] { "character" } },
    new RuleExpectation("Assets/Characters/Hero.png", typeof(Texture2D)),   // matches nothing
});
CollectionAssert.IsEmpty(failures);   // NUnit; every failing row is printed at once
```

- `Verify` returns one message per failing row (empty when all pass) instead of asserting, so the package does not depend on a test framework. Properties you leave unset on a row are not checked; leaving everything unset expects that no rule matches, and setting only `Labels` accepts any match (an address or label-only rules). Set `Kind` to tell `LabelsOnly` from `Address`, or to expect a `Conflict`, `InvalidAddress`, or `BlockedByRuleError` (a row expecting `BlockedByRuleError` does not fail on the exceptions that block the winner). Each failure message starts with the row number and the asset path, and a row whose expectations contradict each other is reported as such.
- Pass every rule class whose interaction you want to test. They are evaluated together, with the same winner selection and label rules as `Apply`, `Validate`, and the dry run: the lowest `Order` wins, and only the winner's labels plus the labels of label-only rules are reported.
- `RuleInspector.Evaluate(rules, context)` returns the full `RuleOutcome` for one asset (kind, group, address, labels, winning rule, messages) when you want to assert on details.
- `RuleInspector.Collect()` returns the entries a single rule's `Configure()` registered, for tests of the rule's shape.
- Not covered, because there is no Addressables project: whether the winning rule's group exists, and the real name of the default group (`GroupDefault()` is reported as `UsesDefaultGroup`). An exception thrown by `Configure()` propagates to the test; an exception thrown by a condition or selector is reported in `Messages` (and fails the `Verify` row).

For a complete example, import **Rule Unit Test Helper** from the Package Manager's Samples tab and refer to the bundled README.

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
          rules.Group("BossAudio") // create this group first — AutoCreateMissingGroups is OFF by default
              .Where(ctx => ctx.IsInFolder("Assets/Audio/Boss"))
              .Address(ctx => $"boss/{ctx.FileNameWithoutExtension}");
      }
  }
  ```

  A **conflict error** occurs only when two or more of the *lowest-`Order`* matching rules tie — in that case no write is performed for that asset (applies to both Apply and Validate). See [Design Decisions: Address Priority and Conflicts](design-decisions.md#address-priority-and-conflicts) for the rationale. An override you intend can be declared so that it is told apart from an accidental one; see [Overlapping Rules](#overlapping-rules-overriddenby).
- **Duplicate addresses across assets**: The conflict above is about one asset with multiple rule candidates; it says nothing about two *different* assets ending up with the same address string — for example, two rules both calling `Naming.FileNameWithoutExtension()` on files that happen to share a name in different folders. `Validate` / `Apply All` separately scan for this and report it as `ValidationStatus.DuplicateAddress`; it never blocks a write, but it is reported as an error when AddressTeller itself would write one of the colliding addresses this run. See [Design Decisions: Address Priority and Conflicts](design-decisions.md#address-priority-and-conflicts) for how this differs from the per-asset conflict above.
- **Labels follow the winning rule**: the labels written for an asset are those of the winning address rule plus those of every matching label-only rule (`AnyGroup()`, or a `Group()` rule with no `Address()`). Labels of an address rule that lost on `Order` are not written. When there is a conflict (a tie) or the write is blocked by a rule error, no labels are written at all. Labels already on the entry are left in place unless you turn on `RemoveStaleManagedLabels` (default: OFF), which removes the labels AddressTeller itself wrote earlier and no rule produces for the asset any more; a label whose name is not in the label ledger (see [Apply & Operations](operations.md#project-settings)) is never removed. See [Design Decisions: Labels Follow the Winning Rule](design-decisions.md#labels-follow-the-winning-rule).
- **No matching rule**: The asset is skipped. If `CleanupStaleEntries` is enabled (see [Apply & Operations](operations.md); it defaults to off), any existing entries in groups AddressTeller owns — the groups where some rule declares `Address()`, see [Design Decisions: Deletions Are Determined by Per-Asset Ownership](design-decisions.md#deletions-are-determined-by-per-asset-ownership) — are removed. This removal only applies when *no* rule matches at all (neither an address nor a label). If a label-only rule (e.g. `AnyGroup()` or a `Group()` rule with no `Address()`) still matches, the entry is **not** removed, and its labels are updated on the existing entry instead. In this label-only case, the existing entry's address and group are left unchanged — they keep whatever value was assigned the last time an address rule matched for that asset. Only its labels are updated, and this happens regardless of which group the existing entry belongs to — including a group no rule declares `Address()` for (label writes are additive and are not gated by ownership; see [Design Decisions: Deletions Are Determined by Per-Asset Ownership](design-decisions.md#deletions-are-determined-by-per-asset-ownership)) — except that a read-only entry or group is left untouched (see [AnyGroup](#anygroup)). If the asset has no existing entry at all, nothing is created — a label-only rule never creates an entry by itself.
- **Group not found**: Results in a `GroupNotFound` error. Groups are not created automatically — create them first in the Addressable Groups window. The opt-in setting **Auto-create missing groups** (`AutoCreateMissingGroups`, default: OFF) makes Apply create the group instead; see [Project Settings](operations.md#project-settings). See also [Design Decisions: Missing Groups Are an Error](design-decisions.md#missing-groups-are-an-error-default).
- **Exception inside a rule**: Only that rule is reported as `RuleError`; processing continues for other rules and other assets. What happens to the asset it was evaluating depends on how the priority of the rule that threw compares to whichever rule actually won for that asset: if the winner has *strictly higher priority* (a lower `Order`) than the rule that threw, its address is still written normally — the failing rule could never have outranked it anyway. But if the rule that threw had an `Order` equal to or lower than (i.e. as high or higher priority as) the winner's, nothing is written for that asset at all — neither the address nor any labels — and `ValidationStatus.BlockedByRuleError` is reported alongside the `RuleError`. This applies only when the failing rule can produce an address (it calls `Address()`); a label-only rule's exception never blocks a write this way, since it was never competing for priority with an address rule. Writing the lower-priority winner's address anyway would silently resolve what is really an ambiguous state — the failed rule might have outranked the winner had it not thrown — the same reasoning behind refusing to guess a tie (see the conflict case above). This blocking behavior only applies once a single winning candidate has actually been chosen for the asset (see the conflict case above for what "chosen" means). If an address-producing rule throws and, as a result, *no* candidate at all is produced for that asset (either it was the only address rule that could have matched, or every other matching address rule also threw), there is no winner to block: the asset falls through to the "no matching rule" case below if nothing else matched at all, or to the label-only case if a label-only rule still matched — in either case, `ValidationStatus.BlockedByRuleError` is not reported, since there was nothing being written that needed blocking. A `Skipped` result in that situation still suppresses `CleanupStaleEntries` for the asset, the same as any other rule exception (see below).
- **Exception inside `Configure()`**: This is different from the previous case — it means the *rule class itself* failed to build its rules (for example, `Group()` throwing because a name contains `/` or `\`, see [Group / Where / Address / Label](#group--where--address--label)), not that an individual rule's predicate or selector threw while evaluating a specific asset. When this happens, that rule class contributes *no* entries for this run — not just the one call that threw, but every other `Group()`/`AnyGroup()` call in the same `Configure()` — whether it ran before or after the one that threw — is discarded too, since the whole set of entries that class built is thrown away together. Other rule classes are unaffected and still evaluate normally. Because the failed class's owned groups can no longer be trusted, stale-entry cleanup (`CleanupStaleEntries`) is skipped for that entire run, not just for that class. The failure is reported once as `ValidationStatus.RuleConfigureFailed` (`Context` is null, since it isn't about any one asset).
