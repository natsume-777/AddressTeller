# RuleUnitTestHelper

A minimal helper for unit-testing `AddressRuleBase` subclasses without a live Addressables project.

`RuleTestHelper` is a thin wrapper around the package's own `AddressTeller.Testing.RuleInspector` public API.
It does not reimplement `IAddressRuleBuilder`, so it always matches the exact builder contract used by the
package's own evaluation pipeline — including `Where()`/`Address()` throwing `InvalidOperationException`
when called a second time on the same group. Note that the *handling* of that exception is intentionally
different here: the package's own evaluation pipeline catches it per-rule and skips the failing rule while
continuing the rest of evaluation, whereas `RuleTestHelper.Collect(rule)` lets it propagate unchanged so a
misused builder call fails the test directly (see step 4 below).

## How to use

1. Import this sample via the Package Manager (Window > Package Manager > AddressTeller > Samples).
2. Add your own test file to `Assets/Samples/AddressTeller/<version>/Rule Unit Test Helper/Tests/`
   (or create a new asmdef that references `AddressTellerSamples.RuleUnitTestHelper.Tests`).
   Asmdef references are not transitive, so if you go the "new asmdef" route, also add an
   explicit reference to `AddressTeller.Core` — otherwise types like `AssetContext` and
   `AddressRuleBase` won't be visible.
3. Write an expectation table: one `RuleExpectation` row per asset, listing what your rule should decide for
   it (`Group`, `Address`, `Labels`; properties you leave unset are not checked, and a row with only a path and a
   type expects that no rule matches; a row with only `Labels` accepts any match; set `Kind` to expect
   `LabelsOnly`, `Conflict`, `InvalidAddress` or `BlockedByRuleError`). Feed the table to NUnit with `[TestCaseSource]` and check each row with
   `RuleTestHelper.Verify(rule, new[] { row })`, which returns one message per failing row (an empty list means
   it passed). `ExampleRuleTest` shows the whole pattern. To test how several rule classes interact, pass them
   all to `RuleInspector.Verify`: they are evaluated together with the same winner selection and label rules
   AddressTeller uses when applying (lowest `Order` wins; only the winner's labels and the labels of label-only
   rules are written).
4. For details beyond the table, use `RuleTestHelper.Evaluate(rule, ctx)`, which returns a `RuleOutcome`
   (`Kind`, `GroupName`, `Address`, `Labels`, `WinningRule`, `Messages`). Use `RuleTestHelper.For(...)` to create
   the `AssetContext`, and `RuleTestHelper.Collect(rule)` to inspect the entries `Configure()` registered.
   Any exception thrown by `Configure()` propagates out of `Evaluate`, `Verify` and `Collect` unchanged, so a
   misused builder call fails the test directly instead of being silently swallowed.
5. If a rule uses `GroupDefault()`, the outcome reports `UsesDefaultGroup` instead of a group name (the
   project's real default group is only known inside Unity); a table row can expect it with
   `UsesDefaultGroup = true`. Likewise, whether a named group actually exists is not checked here. If you
   inspect collected entries directly, use `RuleTestHelper.IsUnresolvedDefaultGroup(entry.GroupName)` rather
   than comparing against a hard-coded string, and pass group names through
   `RuleTestHelper.DisplayGroupName(entry.GroupName)` before printing them — the raw sentinel contains
   unprintable control characters.
6. A folder `AssetContext` (`IsFolder == true`) is only seen by rules whose `Configure()` called
   `IncludeFolders()`; `Evaluate` and `Verify` apply that rule the same way the production pipeline does
   (set `IsFolder = true` on the row). If you write your own loop over collected entries instead, check
   `entry.IncludesFolders` before calling `entry.Predicate(ctx)`.

## Requirements

- Unity Test Framework package (included by default in Unity 6)
- AddressTeller package
