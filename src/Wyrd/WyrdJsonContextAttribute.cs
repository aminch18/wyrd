namespace Wyrd;

/// <summary>
/// Marks your <c>JsonSerializerContext</c> as the one Wyrd uses (pass it to the runtime). Wyrd's analyzers then
/// check that every command, event, state, error and view is declared in it, and offer a code fix when one isn't.
/// </summary>
/// <example>
/// <code>
/// [WyrdJsonContext]
/// [JsonSerializable(typeof(ChargePayment))]
/// public partial class ShopJson : JsonSerializerContext;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class WyrdJsonContextAttribute : Attribute;
