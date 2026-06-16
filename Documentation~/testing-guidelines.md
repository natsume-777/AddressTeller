[日本語](./testing-guidelines.ja.md)

# Testing Guidelines

Rules for writing and reviewing AddressTeller tests (EditMode).
Part 1 covers invariants the entire implementation must uphold; Part 2 covers rules specific to test code.

## Part 1: Design Invariants

- **"Project"-scoped settings must persist under the project**: Settings shown in `Project Settings` must be saved to project-scoped files such as `ProjectSettings/AddressTellerSettings.asset`, making them version-controllable and shareable across team members. Do not store them in machine-wide locations (e.g., personal editor preferences).
- **Cross-asset operations must return results and not suppress them**: Methods that process assets broadly (such as `ApplyAll`/`ValidateAll`) must always return a result such as `IReadOnlyList<ValidationResult>` to the caller. All callers — Menu, CLI, Postprocessor — must surface the returned results to the user via logs or similar.
- **Destructive operations default to safe behavior, determined by per-asset ownership**: Irreversible operations such as entry deletion or label changes must determine "was this entry or label created and managed by AddressTeller?" on a per-asset basis. When ownership is ambiguous, auto-delete options must default to OFF. When a deletion is executed, the target (path, GUID, reason) must be logged individually at Warning level or above.
- **Duplicate calls to Fluent API methods must throw**: Methods like `Where()`, where a second call would create ambiguity, must throw `InvalidOperationException` on the second call — silent overwrites are not allowed.
- **User rule exceptions must be isolated per rule/asset**: User-defined `Configure()` methods, predicates, and address generation functions loaded via reflection must be wrapped in per-rule, per-asset `try/catch`. Exceptions must be reported individually as a dedicated status like `RuleError`, without halting processing for other rules or assets.
- **User-specified file loading requires try/catch and schema validation**: JSON loaded via file dialogs etc. must be wrapped in `try/catch` for parsing, and required fields (e.g., non-empty entry list) must be validated after a successful parse. Unexpected content must not be treated as success.
- **Feature flags must be evaluated consistently across all entry points**: When adding an on/off setting, identify all entry points that should check it (import-time auto-apply, menu, CLI) and route them through a common check function. Avoid asymmetric implementations where only some paths perform the check.
- **Re-entry guards must document their role in comments**: When adding or changing a re-entry prevention flag, leave a comment explaining which call paths can re-enter and how this guard's role differs from any others. When multiple guards exist, test cases must cover each guard's scenario.
- **Cache invariant computations in hot paths**: Do not call expensive computations directly inside `OnGUI`, per-asset loops, or import-time processing — this includes reflection-based rule collection, `AssetDatabase` API calls, and LINQ set operations. Cache in static fields or dictionaries, and invalidate only when necessary (outside of domain reloads).
- **Sorts where tie order is meaningful must be deterministic**: When relative order among equal elements affects outcomes (e.g., evaluation order, conflict message ordering), use `OrderBy(...).ThenBy(...)` with a deterministic key rather than relying on unstable sort behavior.

## Part 2: Test-Specific Rules

- **Test `AddressableAssetSettings` must always be created non-persistent (in memory)**: Tests that write to the project's Addressables settings contaminate the project and leave side effects from `AssetPostprocessor` integration. Never write production `.asset` files to disk from tests. Use `AddressTellerTestSettingsFactory.CreateInMemory(configFolder, ...)` (`Tests/Editor/AddressTellerTestSettingsFactory.cs`).
- **APIs that reference `ConfigFolder` require test folder path injection**: When testing APIs that internally reference `ConfigFolder` (such as `ApplyAll`/`ValidateAll`), inject a test folder path into the settings. Not required when only testing APIs that do not reference `ConfigFolder`.
- **Group creation must not fire static change events**: When creating groups in tests, pass `postEvent: false` to avoid firing Addressables' static change events.
- **Do not break the per-assembly pollution detection guard**: Each test assembly has `AddressTellerAddressablesPollutionGuard` (`[SetUpFixture]`, `Tests/Editor/AddressTellerAddressablesPollutionGuard.cs`) permanently in place. It compares the serialized state of the production Addressables settings before and after each test run, failing if any diff is found. New tests must not break this guard — use non-persistent settings and clean up after each test.

It is normal for 2 tests to be skipped due to environment factors when running EditMode tests.
