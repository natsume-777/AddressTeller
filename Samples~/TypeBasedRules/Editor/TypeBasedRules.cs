using AddressTeller;
using UnityEngine;

namespace AddressTellerSamples
{
    /// <summary>
    /// Sample that routes assets to different groups and labels based on asset type.
    /// Import via Package Manager > Samples to use.
    ///
    /// Prerequisites:
    ///   - Four Addressable Groups named "Prefabs", "Textures", "Audio", and "Configs" must exist.
    ///   - Assets must be placed under Assets/Demo/.
    /// </summary>
    public sealed class TypeBasedRules : AddressRuleBase
    {
        private const string RootPath = "Assets/Demo/";

        public override int Order => 0;

        public override void Configure(IAddressRuleBuilder rules)
        {
            // Addresses are prefixed by type (e.g. "prefab/Player") rather than using
            // FileNameWithoutExtension alone. Without the prefix, a prefab and a texture that happen to
            // share a base file name in the same folder (e.g. "Player.prefab" and "Player.png") would both
            // resolve to address "Player" -- Validate / Apply All would report that as a duplicate address.

            // Prefab (GameObject) -> Prefabs group, label "prefab"
            rules.Group("Prefabs")
                .Where(ctx => ctx.Path.StartsWith(RootPath) && ctx.Type == typeof(GameObject))
                .Address(ctx => $"prefab/{ctx.FileNameWithoutExtension}")
                .Label("prefab");

            // Texture -> Textures group, label "texture"
            rules.Group("Textures")
                .Where(ctx => ctx.Path.StartsWith(RootPath) && ctx.Type == typeof(Texture2D))
                .Address(ctx => $"texture/{ctx.FileNameWithoutExtension}")
                .Label("texture");

            // AudioClip -> Audio group, label "audio"
            rules.Group("Audio")
                .Where(ctx => ctx.Path.StartsWith(RootPath) && ctx.Type == typeof(AudioClip))
                .Address(ctx => $"audio/{ctx.FileNameWithoutExtension}")
                .Label("audio");

            // Any ScriptableObject subclass -> Configs group, label "config"
            // ctx.Type is the exact runtime type, so use IsAssignableFrom to include subclasses.
            rules.Group("Configs")
                .Where(ctx => ctx.Path.StartsWith(RootPath)
                           && typeof(ScriptableObject).IsAssignableFrom(ctx.Type))
                .Address(ctx => $"config/{ctx.FileNameWithoutExtension}")
                .Label("config");
        }
    }
}
