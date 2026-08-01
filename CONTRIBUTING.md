[日本語](./CONTRIBUTING.ja.md)

# Contributing

Before making changes to AddressTeller, please review the following documents on design decisions and testing guidelines.

## Documentation

- [Architecture](Documentation~/architecture.md) — layer structure, folder responsibilities, rule evaluation flow
- [Design Decisions](Documentation~/design-decisions.md) — how addresses, labels, and groups work and the public API / internal implementation boundary
- [Testing Guidelines](Documentation~/testing-guidelines.md) — design invariants the implementation must uphold, and rules specific to test code
- [Compatibility Policy](Documentation~/compatibility.md) — what is and isn't covered by SemVer guarantees

## Type Naming

### Namespaces and assemblies

- Core domain types: namespace `AddressTeller`, assembly `AddressTeller.Core`.
- Addressables-facing types: namespace `AddressTeller.Editor`, assembly `AddressTeller.Editor`.
- Test-support types: namespace `AddressTeller.Testing`.

### The `AddressTeller` prefix on public types

The default is **no prefix** — the namespace already qualifies the type.

Add the prefix only when the type sits on the package boundary, which means one of:

1. **An entry point.** A type a user names directly to drive the package: menu hosts,
   the `AssetPostprocessor` hook, the CLI argument type, and the service facades for the
   package's primary operations (apply / validate / clear / snapshot / settings /
   report writing). These names appear in `-executeMethod` strings, in documentation,
   and in the Editor's own global lists, where an unqualified name would be ambiguous.
2. **A serialized artifact root.** The top-level object of a file the package writes and
   a user may keep in version control or feed to CI (`AddressTellerReport`,
   `AddressTellerSnapshot`).

Everything else keeps a bare, descriptive name:

- domain models — `LogicalBundle`, `BundleDistribution`
- result and value types — `ValidationResult`, `ClearedEntry`, `DryRunResult`, `SnapshotDiff`
- enums — `ValidationStatus`, `ClearScope`, `ReportFormat`
- interfaces — `IProgressReporter`, `IAddressRuleBuilder`
- helpers already scoped by a specific feature name — `BundleDistributionSerializer`,
  `BundleModeReader`

Sub-objects of a serialized artifact root take **the root's name**, not the prefix
independently: `AddressTellerReportSummary`, `AddressTellerReportIssue`. Where the
feature area is unambiguous on its own, the shorter area name is enough:
`SnapshotEntry`, `BundleDistributionReportEntry`. When these two options conflict
for a new type, default to inheriting the root's name; drop to the shorter area
name only if an existing sibling type in the same feature area has already
established that shorter form.

Do not put a layering term such as `Dto`, `Impl`, or `Model` in a public type name. It
describes the package's internals rather than anything the user can act on. Name the
serialized shape after what it is a serialized shape *of*.

### Exception: the rule-authoring DSL

`Match`, `Naming`, `AssetCondition`, `AssetContext`, and the `I*RuleBuilder` interfaces
stay short even where the rule above would call for a prefix. `Match` in particular
collides with `System.Text.RegularExpressions.Match`. These names are typed on nearly
every line of a rule class, so brevity outweighs disambiguation; the collision is
resolved on the user's side with a `using` alias
(see [Writing Rules](Documentation~/writing-rules.md#match-static-class)).

### `internal` types

Existing `internal` types keep their current names, including the `AddressTeller`-prefixed
ones (`AddressTellerApplier`, `AddressTellerApplyFlow`, `AddressTellerReportBuilder`, ...).
Renaming them is churn with no user-visible benefit. New `internal` types should follow the
same default as public ones — no prefix unless it genuinely aids readability at the call
site. This is a preference, not a requirement.

### Adding a public type

Naming a new public type with the prefix is a claim that it belongs on the package
boundary. Check it against 1 and 2 above; if it is neither, drop the prefix. The current
public surface — including which types carry the prefix — is recorded in
`Tests/Editor/PublicApiApproval/PublicAPI.*.approved.txt`, and renaming any of them is a
breaking change (see [Compatibility Policy](Documentation~/compatibility.md)).

## Running Tests

Open this project in the Unity Editor and run EditMode tests via the Test Runner.
A normal run results in skip 2 / fail 0.
