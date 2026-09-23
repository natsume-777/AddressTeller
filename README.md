[日本語](./README.ja.md)

# AddressTeller

A tool that assigns addresses and labels for Unity Addressables **in C# code**, and can keep them
up to date automatically once you turn that on.
Rules are defined code-first, not through ScriptableObjects or Inspector UI.

## Requirements

- Unity 6000.0 or later
- com.unity.addressables 2.8.1 or later

## Installation

In the Package Manager, use `Add package from git URL...` and enter:

```
https://github.com/natsume-777/AddressTeller.git
```

Or add it directly to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.natsume777.addressteller": "https://github.com/natsume-777/AddressTeller.git"
  }
}
```

Both forms above track the default branch, so a later `Update` can pull in breaking changes.
To pin a released version, append the release tag to the URL. While the version is `0.x`, the
[Compatibility Policy](Documentation~/compatibility.md) does not yet apply; breaking changes may
land in any minor release. Pinning to a tag is therefore strongly recommended:

```
https://github.com/natsume-777/AddressTeller.git#v<X.Y.Z>
```

See [GitHub Releases](https://github.com/natsume-777/AddressTeller/releases) for available tags.

**Note:** Release tags exist only for version 0.4.0 and later; tag-based pinning is not available for earlier versions.

## Quick Start

New to Addressables? An **address** is the string key used to load an asset at runtime (`Addressables.LoadAssetAsync<GameObject>("Player")`), a **label** is a freeform tag for filtering/grouping assets across addresses, and a **group** is an Addressables container that controls how its assets are bundled. Installing AddressTeller pulls in `com.unity.addressables` as a package dependency, but Addressables itself still needs to be initialized once per project: open `Window > Asset Management > Addressables > Groups` and create the settings if prompted.

Create a class that inherits `AddressRuleBase` and place it under an `Editor` folder — it will be collected automatically via reflection.
Define rules by chaining `Group().Where().Address().Label()` inside `Configure()`.

```csharp
using AddressTeller;
using UnityEngine;

public sealed class GameAddressRules : AddressRuleBase
{
    public override int Order => 0;

    public override void Configure(IAddressRuleBuilder rules)
    {
        rules.Group("Characters")
            .Where(ctx => ctx.Path.StartsWith("Assets/Game/Characters/")
                       && ctx.Type == typeof(GameObject))
            .Address(ctx => ctx.FileNameWithoutExtension)   // "Player"
            .Label("character");
    }
}
```

Note that `ctx.FileNameWithoutExtension` gives the same address to any two files that happen to share a name in different folders — `Validate` / `Apply All` will flag that as a duplicate address if it happens (see [Writing Rules](Documentation~/writing-rules.md#evaluation-rules-and-behavior)).

Run `Tools/AddressTeller/Apply All` to assign addresses and labels to matching assets. `Apply All` creates or moves the underlying Addressable entry itself — you don't need to mark each asset Addressable by hand first. The `Characters` group must already exist, though: create it beforehand in `Window > Asset Management > Addressables > Groups` (a missing group name is treated as an error, not auto-created by default). Before writing anything, `Apply All` shows a confirmation dialog summarizing what will change and saves an automatic safety snapshot you can restore from (`Tools/AddressTeller/Undo Last Apply`).

As long as your `Where()` conditions don't match assets outside the groups you intend to hand over, AddressTeller only **deletes entries** in a group where one of your rules declares `Address()`, and only once you opt into that (step 3 below) — within such a group, even a manually registered entry is removed once no rule matches it — so you can adopt it for one group at a time without affecting the rest of an existing Addressables setup. Any asset a rule *does* match, however, is unconditionally **moved** into that rule's group regardless of which group it currently belongs to (even a manually managed one) — so scope your `Where()` conditions to the assets you actually want AddressTeller to own. See [Design Decisions](Documentation~/design-decisions.md#deletions-are-determined-by-per-asset-ownership) for details.

AddressTeller is meant to be adopted in three steps, each safe to stop at:

1. **Run `Apply All` by hand** (as above) and review what it changes as you add or adjust rules. Nothing runs automatically yet, and nothing is ever deleted at this stage.
2. **Once you're happy with what `Apply All` does, turn on "Auto-apply on import"** in `Project Settings > AddressTeller`. From then on, an `AssetPostprocessor` runs automatically on every asset import, move, or deletion: for an imported or moved asset it re-runs rule evaluation, so routine asset imports keep addresses and labels current without you running `Apply All` by hand. (Removing an asset's entry when it is deleted is done by Addressables itself, independently of this setting.) This step still never deletes anything on AddressTeller's own initiative.
3. **Once you know which groups your rules own, turn on "Remove unmatched entries"** to let AddressTeller delete owned-group entries no rule matches anymore (including ones you registered manually — see [Design Decisions](Documentation~/design-decisions.md#deletions-are-determined-by-per-asset-ownership)). Turn it on and run `Tools/AddressTeller/Apply All` from the menu right afterward, so any deletions that built up while it was off go through the confirmation dialog and safety snapshot from step 1 instead of happening silently on the next import. While it's off, `Validate` and the Preview windows report what *would* be deleted as a non-blocking notice, so you can check before opting in.

Both settings from steps 2 and 3 default to off. See [Apply & Operations](Documentation~/operations.md) for every apply method, CI integration, and these settings in detail.

To assign to the Addressables DefaultGroup, use `GroupDefault()` instead of `Group("name")` — it follows DefaultGroup renames automatically. See [Writing Rules](Documentation~/writing-rules.md#groupdefault) for details.

`Order` also doubles as a priority: write a broad rule with a high `Order` value and a narrower rule with a lower one, and the narrower rule's address wins whenever both match the same asset. See [Writing Rules: Address priority and conflicts](Documentation~/writing-rules.md#evaluation-rules-and-behavior) for an example.

If you define rule classes in a custom assembly, add `AddressTeller.Core` to your asmdef's `references` — the whole rule-authoring surface (`AddressRuleBase`, `IAddressRuleBuilder`, `Match`, `Naming`, `AssetContext`) lives there. Add `AddressTeller.Editor` as well only if the same assembly also calls the operational APIs (`AddressTellerService`, `ValidationResult`, snapshots, reports); those are in `AddressTeller.Editor` but expose Core types in their signatures, so referencing `AddressTeller.Editor` always means referencing `AddressTeller.Core` too.

Do not place rule classes in an assembly whose asmdef references `nunit.framework` (e.g. an EditMode test assembly) — AddressTeller's rule collection silently excludes any such assembly (no warning), so those rules would never run. Put them in an ordinary Editor asmdef instead.

## Documentation

- [Writing Rules](Documentation~/writing-rules.md) — `AddressRuleBase` authoring, `Match`/`Naming` helpers, `AssetContext`, evaluation behavior
- [Apply & Operations](Documentation~/operations.md) — apply methods, CI integration, Project Settings, snapshots, samples
- [Design Decisions](Documentation~/design-decisions.md) — why addresses, labels, and groups behave the way they do
- [Architecture](Documentation~/architecture.md) — layer structure, folder responsibilities, rule evaluation flow
- [Compatibility Policy](Documentation~/compatibility.md) — what is and isn't covered by SemVer guarantees (worth reading once you're integrating in CI or upgrading versions; safe to skip while you're just trying AddressTeller out)
- [Contributing](CONTRIBUTING.md)

## Background

Address and label design is typically an engineer's responsibility, yet rule-definition approaches based on ScriptableObject + Inspector UI cap expressiveness at "what the UI can represent." Group references stored as GUIDs break on rename or deletion and produce noisy diffs.

AddressTeller moves rule definitions into C# code to provide:

- **Unlimited expressiveness** — path parsing, external data loading, any C# logic
- **Clean diffs** — no `.asset` files to manage, just code
- **Full IDE support** — autocomplete, refactoring, and unit tests as ordinary C#

Data-driven configurations are also supported by choosing the loading strategy freely in code.

## License

[MIT](LICENSE)
