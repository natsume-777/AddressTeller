using System.Runtime.CompilerServices;

// Editorアセンブリのinternal全般をテストへ公開する。Core側のinternalは`Editor/Core/AssemblyInfo.cs`が別途宣言している。
[assembly: InternalsVisibleTo("AddressTeller.Editor.Tests")]
