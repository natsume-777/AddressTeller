# RuleUnitTestHelper

A minimal helper for unit-testing `AddressRuleBase` subclasses without a live Addressables project.

`RuleTestHelper` is a thin wrapper around the package's own `AddressTeller.Testing.RuleInspector` public API.
It does not reimplement `IAddressRuleBuilder`, so it always matches the exact builder contract used by the
package's own evaluation pipeline — including `Where()`/`Address()` throwing `InvalidOperationException`
when called a second time on the same group. Note that the *handling* of that exception is intentionally
different here: the package's own evaluation pipeline catches it per-rule and skips the failing rule while
continuing the rest of evaluation, whereas `RuleTestHelper.Collect(rule)` lets it propagate unchanged so a
misused builder call fails the test directly (see step 3 below).

## How to use

1. Import this sample via the Package Manager (Window > Package Manager > AddressTeller > Samples).
2. Add your own test file to `Assets/Samples/AddressTeller/<version>/Rule Unit Test Helper/Tests/`
   (or create a new asmdef that references `AddressTellerSamples.RuleUnitTestHelper.Tests`).
   Asmdef references are not transitive, so if you go the "new asmdef" route, also add an
   explicit reference to `AddressTeller.Core` — otherwise types like `AssetContext` and
   `AddressRuleBase` won't be visible.
3. Use `RuleTestHelper.For(...)` to create `AssetContext` instances and
   `RuleTestHelper.Collect(rule)` to inspect the entries produced by `Configure()`.
   Any exception thrown by `Configure()` propagates out of `Collect(rule)` unchanged, so a
   misused builder call fails the test directly instead of being silently swallowed.
4. If a rule uses `GroupDefault()`, check `RuleTestHelper.IsUnresolvedDefaultGroup(entry.GroupName)` rather than
   comparing against a hard-coded string — the real sentinel value is an internal implementation
   detail of the package. When you need to show a group name in an assertion message or log output, pass it
   through `RuleTestHelper.DisplayGroupName(entry.GroupName)` first — the raw sentinel contains unprintable
   control characters that would otherwise show up garbled in a failed test's output.
5. If you build your own evaluation loop over the collected entries and pass it a folder `AssetContext`
   (`IsFolder == true`), check `entry.IncludesFolders` before calling `entry.Predicate(ctx)` — see
   `ExampleRuleTest.FindFirst` for the exact check. A rule only sees folders if its `Configure()` called
   `IncludeFolders()`; the production evaluation pipeline skips `Predicate` entirely otherwise, and a helper
   loop that skips this check can call a rule's `Predicate` with a folder it was never written to handle.

## Requirements

- Unity Test Framework package (included by default in Unity 6)
- AddressTeller package
