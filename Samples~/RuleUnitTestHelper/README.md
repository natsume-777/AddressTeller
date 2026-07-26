# RuleUnitTestHelper

A minimal helper for unit-testing `AddressRuleBase` subclasses without a live Addressables project.

## How to use

1. Import this sample via the Package Manager (Window > Package Manager > AddressTeller > Samples).
2. Add your own test file to `Assets/Samples/AddressTeller/.../RuleUnitTestHelper/Tests/`
   (or create a new asmdef that references `AddressTellerSamples.RuleUnitTestHelper.Tests`).
   Asmdef references are not transitive, so if you go the "new asmdef" route, also add an
   explicit reference to `AddressTeller.Core` — otherwise types like `AssetContext` and
   `AddressRuleBase` won't be visible.
3. Use `RuleTestHelper.For(...)` to create `AssetContext` instances and
   `RuleTestHelper.Collect(rule)` to inspect the entries produced by `Configure()`.

## Requirements

- Unity Test Framework package (included by default in Unity 6)
- AddressTeller package
