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
                // Where() can only be called once per group. Build the condition from Match.* helpers and
                // combine them with And() / Or() / Not(); they describe themselves in Explain and in error messages.
                .Where(Match.InFolder("Assets/Demo/Characters").And(Match.OfType<GameObject>()))
                // Address: file name without extension
                .Address(ctx => ctx.FileNameWithoutExtension)
                // Label() can be called any number of times. These labels are written only when this rule's address wins for the asset.
                .Label("character")
                .Label("humanoid");

            // Prefabs under Items/
            // A group rule without Address() is valid — it only assigns labels without issuing an address.
            // Here Address() is provided, so the address will be the file name without extension.
            rules.Group("MyGroup")
                .Where(Match.InFolder("Assets/Demo/Items").And(Match.OfType<GameObject>()))
                .Address(ctx => ctx.FileNameWithoutExtension)
                .Label("item");
        }
    }
}
