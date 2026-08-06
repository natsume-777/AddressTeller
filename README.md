[日本語](./README.ja.md)

# AddressTeller

A tool that automatically assigns addresses and labels for Unity Addressables **in C# code**.
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

Run `Tools/AddressTeller/Apply All` to assign addresses and labels to matching assets. `Apply All` creates or moves the underlying Addressable entry itself — you don't need to mark each asset Addressable by hand first. The `Characters` group must already exist, though: create it beforehand in `Window > Asset Management > Addressables > Groups` (a missing group name is treated as an error, not auto-created by default).

As long as your `Where()` conditions don't match assets outside the groups you intend to hand over, AddressTeller only **deletes entries or adds labels** in groups referenced by at least one of your rules, so you can adopt it for one group at a time without affecting the rest of an existing Addressables setup. Any asset a rule *does* match, however, is unconditionally **moved** into that rule's group regardless of which group it currently belongs to (even a manually managed one) — so scope your `Where()` conditions to the assets you actually want AddressTeller to own. See [Design Decisions](Documentation~/design-decisions.md#deletions-are-determined-by-per-asset-ownership) for details.

By default, an `AssetPostprocessor` also re-runs rule evaluation automatically whenever an asset is imported, moved, or deleted ("Auto-apply on import", ON by default) — so once a rule is in place, routine asset imports can trigger it without running `Apply All` manually. This includes deleting entries that no longer match any rule (`CleanupStaleEntries`, also ON by default); see [Design Decisions](Documentation~/design-decisions.md#deletions-are-determined-by-per-asset-ownership). Both settings can be turned off in Project Settings; see [Apply & Operations](Documentation~/operations.md) for all apply methods and these settings.

To assign to the Addressables DefaultGroup, use `GroupDefault()` instead of `Group("name")` — it follows DefaultGroup renames automatically. See [Writing Rules](Documentation~/writing-rules.md#groupdefault) for details.

If you define rule classes in a custom assembly, add `AddressTeller.Core` to your asmdef's `references` — the whole rule-authoring surface (`AddressRuleBase`, `IAddressRuleBuilder`, `Match`, `Naming`, `AssetContext`) lives there. Add `AddressTeller.Editor` as well only if the same assembly also calls the operational APIs (`AddressTellerService`, `ValidationResult`, snapshots, reports); those are in `AddressTeller.Editor` but expose Core types in their signatures, so referencing `AddressTeller.Editor` always means referencing `AddressTeller.Core` too.

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
