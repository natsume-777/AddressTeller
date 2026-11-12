using System;
using System.Collections.Generic;

namespace AddressTeller.Testing
{
    /// <summary>
    /// One row of an expectation table for <see cref="RuleInspector.Verify"/>: an asset, and what the rules
    /// are expected to decide for it. Properties you leave unset are not checked.
    /// </summary>
    /// <remarks>
    /// When <see cref="Kind"/> is not set it is inferred: <see cref="RuleOutcomeKind.Address"/> if
    /// <see cref="Address"/>, <see cref="Group"/> or <see cref="UsesDefaultGroup"/> = true is set; otherwise, if
    /// <see cref="Labels"/> is non-empty, any match (<see cref="RuleOutcomeKind.Address"/> or
    /// <see cref="RuleOutcomeKind.LabelsOnly"/>) is accepted; otherwise <see cref="RuleOutcomeKind.None"/>.
    /// Set <see cref="Kind"/> explicitly to tell <see cref="RuleOutcomeKind.LabelsOnly"/> from
    /// <see cref="RuleOutcomeKind.Address"/>, or to expect a conflict, an invalid address, or a rule error.
    /// A row whose properties contradict each other (for example <see cref="Address"/> together with
    /// <c>Kind = Conflict</c>) is reported by <see cref="RuleInspector.Verify"/> as a contradictory expectation.
    /// A row that expects <see cref="RuleOutcomeKind.BlockedByRuleError"/> does not fail because of the exceptions
    /// that block the winner; any other rule exception still fails the row.
    /// </remarks>
    public sealed class RuleExpectation
    {
        /// <summary>Asset path of the asset being evaluated (e.g. "Assets/Characters/Hero.prefab").</summary>
        public string Path { get; }

        /// <summary>Asset type of the asset being evaluated (e.g. typeof(GameObject)).</summary>
        public Type Type { get; }

        /// <summary>True when the asset is a folder. Folders are only seen by rules that call <c>IncludeFolders()</c>.</summary>
        public bool IsFolder { get; set; }

        /// <summary>The expected verdict. When null, it is inferred (see the remarks of this type).</summary>
        public RuleOutcomeKind? Kind { get; set; }

        /// <summary>The expected group name of the winning rule. Not checked when null.</summary>
        public string Group { get; set; }

        /// <summary>
        /// Set to true to expect the winning rule to target the project's default group
        /// (<see cref="IAddressRuleBuilder.GroupDefault"/>), or to false to expect it not to. Not checked when
        /// null. The default group's actual name is not known here.
        /// </summary>
        public bool? UsesDefaultGroup { get; set; }

        /// <summary>The expected address of the winning rule. Not checked when null.</summary>
        public string Address { get; set; }

        /// <summary>
        /// The expected labels, compared as a set (order and duplicates are ignored). Not checked when null;
        /// an empty collection expects that no label is written.
        /// </summary>
        public IReadOnlyCollection<string> Labels { get; set; }

        /// <summary>Creates an expectation for the asset at <paramref name="path"/> with the given <paramref name="type"/>.</summary>
        /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
        public RuleExpectation(string path, Type type)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path must not be empty.", nameof(path));
            Path = path;
            Type = type ?? throw new ArgumentNullException(nameof(type));
        }

        /// <summary>The path, followed by the expected kind when one is set. Makes a row readable in test output.</summary>
        public override string ToString() => Kind.HasValue ? $"{Path} ({Kind.Value})" : Path;
    }
}
