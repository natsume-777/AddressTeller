using AddressTeller;
using UnityEngine;

namespace AddressTellerSamples
{
    /// <summary>
    /// Minimal sample for address/label assignment rules.
    /// Import via Package Manager > Samples to use.
    ///
    /// Prerequisites:
    ///   - An Addressable Group named "MyGroup" must exist.
    ///   - Assets must be placed under Assets/Demo/Characters/ and Assets/Demo/Items/.
    ///
    /// Placement note:
    ///   AddressRuleBase and related types are Editor-only, so this script
    ///   must be placed inside a folder named "Editor".
    /// </summary>
    public sealed class ExampleRules : AddressRuleBase
    {
        // Evaluation order. When multiple rule classes exist, lower values are evaluated first.
        // Duplicate Order values across classes produce a warning during Apply All / Validate.
        public override int Order => 0;

        public override void Configure(IAddressRuleBuilder rules)
        {
            // Multiple Group() calls can be made within a single Configure() to define multiple rule entries.

            // Prefabs under Characters/
            rules.Group("MyGroup")
                // Where() can only be called once per group. Combine multiple conditions with &&.
                .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Characters/")
                           && ctx.Type == typeof(GameObject))
                // Address: file name without extension
                .Address(ctx => ctx.FileNameWithoutExtension)
                // Label() can be called any number of times. Labels from all matching rules accumulate.
                .Label("character")
                .Label("humanoid");

            // Prefabs under Items/
            // A group rule without Address() is valid — it only assigns labels without issuing an address.
            // Here Address() is provided, so the address will be the file name without extension.
            rules.Group("MyGroup")
                .Where(ctx => ctx.Path.StartsWith("Assets/Demo/Items/")
                           && ctx.Type == typeof(GameObject))
                .Address(ctx => ctx.FileNameWithoutExtension)
                .Label("item");
        }
    }
}
