namespace Wyrd;

/// <summary>
/// Marks an abstract record as a closed set of cases (its nested sealed records).
/// Wyrd.Analyzers will require every <c>switch</c> over it to be exhaustive, Rust-style.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UnionAttribute : Attribute;
