[日本語](./architecture.ja.md)

# Architecture

This document covers AddressTeller's code organization, layer dependencies, and rule evaluation flow.
For the rationale behind the public API / internal implementation boundary, see [Public API and Internal Implementation Boundary](design-decisions.md#public-api-and-internal-implementation-boundary) (not duplicated here).

## Assembly and Namespaces

All editor code is consolidated into a single assembly `AddressTeller.Editor` (asmdef name / rootNamespace), but it spans two namespaces:

- Types under `Editor/Core` — the rule-definition surface and evaluation engine (`AddressRuleBase`, `Match`, `Naming`, `AssetContext`, `AddressResolution`, `RuleEvaluator`, etc.) — use `namespace AddressTeller` (no `.Editor` suffix).
- Types under `Editor/Application` and `Editor/EntryPoints` use `namespace AddressTeller.Editor`.

The `Editor/Core` / `Editor/Application` / `Editor/EntryPoints` folder split represents a logical layer boundary, not an assembly boundary (only Core uses a different namespace, as noted above). The one-way dependency direction between layers (described below) is enforced by folder conventions and `internal`/`public` access modifiers, not by the compiler — the compiler only prevents access to `internal` types from outside the assembly.

The test assembly `AddressTeller.Editor.Tests` can directly reference `internal` types inside `AddressTeller.Editor` (in either namespace) via the `InternalsVisibleTo` attribute in `Editor/AssemblyInfo.cs`. `InternalsVisibleTo` is per-assembly, not per-namespace.

## Layer Dependencies

```
EntryPoints  ── Postprocessor / Menu / CLI / ProjectSettings / Windows / TreeViews
     │ depends on
     ▼
Application  ── Service / Applier / RuleCollector / Pipeline / Snapshot / Report / Settings
     │ depends on
     ▼
Core         ── Rule-definition surface / Evaluation engine (no Addressables dependency)
```

Dependencies flow one-way from upper layers to lower layers. Core does not reference Application or EntryPoints.

- **Core**: Pure logic with no dependency on Addressables (rule-definition surface and evaluation engine).
- **Application**: Addressables integration layer — handles `AddressableAssetSettings` and other Addressables APIs.
- **EntryPoints**: Unity Editor UI, hooks, and CLI.

Each layer contains both public surface and internal implementation. For example, Core hosts both the public rule-definition surface (`AddressRuleBase`, etc.) and the internal evaluation engine (`RuleEvaluator`, etc.). Similarly, Application hosts both the public `AddressTellerService`/`AddressTellerSettings` and the internal `AddressTellerApplier`/`RuleEvaluationPipeline`. For the full list of which types are public and which are internal, see [Public API and Internal Implementation Boundary](design-decisions.md#public-api-and-internal-implementation-boundary).

## Folder Responsibilities

### Editor/Core

Domain model and evaluation engine with no dependency on Addressables. The assembly is `AddressTeller.Editor`, but types here use `namespace AddressTeller` (no `.Editor` suffix).

- Rule-definition surface (public): `AddressRuleBase`, `IAddressRuleBuilder`, `Match`, `AssetCondition`, `Naming`, `AssetContext`, `AddressRuleEntry`, etc.
- Evaluation implementation (internal): `RuleEvaluator`, `AddressRuleBuilderImpl`, `AddressResolution`, `RuleExplanation`, etc.

### Editor/Application

Addressables integration layer — responsible for rule collection, pipeline execution, writing to Addressables, snapshots, reports, and settings. Namespace: `AddressTeller.Editor`.

- Public: execution entry points (`AddressTellerService`, `AddressTellerSettings`), snapshot types, progress reporting, result types, report DTOs
- Internal: assembly internals such as `AddressTellerApplier`, `RuleEvaluationPipeline`, `RuleCollector`

### Editor/EntryPoints

Unity Editor hooks, UI, and CLI. Namespace: `AddressTeller.Editor`. Contains `AddressTellerPostprocessor` (auto-apply on import), `AddressTellerMenu`/`AddressTellerCliArgs` (menu operations and CI integration), `AddressTellerProjectSettings`, and various Window/TreeView types.

This layer only calls the public surface of Application and carries no domain logic itself.

### Tests/Editor

NUnit EditMode tests. Uses `InternalsVisibleTo` to directly test `internal` types in Application/Core. The `Tests` folder is treated as a UPM test assembly and is not included in the distribution.

### Samples~

Sample rules for users (`BasicRules`, `FolderBasedRules`, `TypeBasedRules`). Per UPM conventions, `Samples~` is not imported by default; users import individual samples from the Package Manager.

## Rule Injection and Evaluation Flow

1. EntryPoints (Postprocessor/Menu/CLI) call `AddressTellerService.ApplyAll(...)`/`ValidateAll(...)`, passing only the target paths (`paths`) and `AddressableAssetSettings` (`settings`). EntryPoints do not collect rules themselves.
2. `ApplyAll(paths, settings)`/`ValidateAll(paths, settings)` delegate to the progress-reporting overload `ApplyAll(paths, settings, progress)`/`ValidateAll(paths, settings, progress)`. This overload calls `RuleCollector.CollectEnabledRules()` to collect rules via reflection. `RuleCollector` scans loaded assemblies for concrete types that inherit `AddressRuleBase` and have a no-arg constructor, sorting them ascending by Order and then deterministically by type FullName (Ordinal comparison) for ties. Test assemblies (those referencing nunit.framework) are excluded. The result is cached statically until the next domain reload. `CollectEnabledRules()` further filters the cache to exclude rule classes disabled in Project Settings.
3. The collected rule list (`IReadOnlyList<AddressRuleBase>`) is injected into the bottom-most overload `ApplyAll(paths, settings, progress, rules)` via the `rules` parameter. This `rules` parameter is the rule injection point — tests and scoped previews can pass an arbitrary rule set here directly, bypassing `RuleCollector`'s reflection-based collection.
4. Service calls `RuleEvaluationPipeline.BuildSetup` once outside the asset loop to build `EvaluationSetup` (invariant computations such as the existing group name set and managed group set), then evaluates each asset through Core's evaluation engine and passes results to `AddressTellerApplier` (for Apply) or the validation logic (for Validate).

Simplified sequence:

```
EntryPoint → Service.ApplyAll(paths, settings)
                 └→ ApplyAll(paths, settings, progress)   ← progress-reporting overload
                       └→ RuleCollector.CollectEnabledRules()   [reflection + Order sort + disabled filter, cached]
                       └→ ApplyAll(paths, settings, progress, rules)   ← rule injection point
                             └→ RuleEvaluationPipeline.BuildSetup(...)
                             └→ Core evaluation engine → Applier / Validate
```
