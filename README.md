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

## Quick Start

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

Run `Tools/AddressTeller/Apply All` to assign addresses and labels to matching assets.
The `Characters` group must be created in the Addressable Groups window beforehand (a missing group name is treated as an error).
To assign to the Addressables DefaultGroup, use `GroupDefault()` instead of `Group("name")` — it follows DefaultGroup renames automatically. See [Writing Rules](Documentation~/writing-rules.md#groupdefault) for details.

If you define rule classes in a custom assembly, ensure your asmdef's `references` includes both `AddressTeller.Core` and `AddressTeller.Editor` (the public APIs expose types from both assemblies).

## Documentation

- [Writing Rules](Documentation~/writing-rules.md) — `AddressRuleBase` authoring, `Match`/`Naming` helpers, `AssetContext`, evaluation behavior
- [Apply & Operations](Documentation~/operations.md) — apply methods, CI integration, Project Settings, snapshots, samples
- [Design Decisions](Documentation~/design-decisions.md) — why addresses, labels, and groups behave the way they do
- [Architecture](Documentation~/architecture.md) — layer structure, folder responsibilities, rule evaluation flow
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
