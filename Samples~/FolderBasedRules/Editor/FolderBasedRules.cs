using AddressTeller;

namespace AddressTellerSamples
{
    /// <summary>
    /// Sample that maps the folder hierarchy directly to addresses and labels.
    /// Import via Package Manager > Samples to use.
    ///
    /// Prerequisites:
    ///   - An Addressable Group named "FolderAssets" must exist.
    ///   - Assets must be placed in subfolders under Assets/Demo/
    ///     (e.g. Assets/Demo/Characters/Enemies/Goblin.prefab).
    /// </summary>
    public sealed class FolderBasedRules : AddressRuleBase
    {
        private const string RootPath = "Assets/Demo/";

        public override int Order => 0;

        public override void Configure(IAddressRuleBuilder rules)
        {
            // Targets all assets placed in subfolders under Assets/Demo/
            rules.Group("FolderAssets")
                .Where(ctx => ctx.Path.StartsWith(RootPath)
                           && ctx.Path.Substring(RootPath.Length).Contains("/"))
                // Address: path without extension, relative to Assets/Demo/
                // Example: "Assets/Demo/Characters/Enemies/Goblin.prefab" -> "Characters/Enemies/Goblin"
                .Address(ctx => ctx.Directory.Substring(RootPath.Length - 1).TrimStart('/')
                             + "/" + ctx.FileNameWithoutExtension)
                // Label: the immediate subfolder name under Assets/Demo/
                // Example: "Assets/Demo/Characters/Enemies/Goblin.prefab" -> "Characters"
                .Label(ctx => ctx.Path.Substring(RootPath.Length).Split('/')[0]);
        }
    }
}
