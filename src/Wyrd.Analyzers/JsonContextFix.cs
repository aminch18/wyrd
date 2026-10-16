namespace Wyrd.Analyzers;

/// <summary>Diagnostic properties read by the JSON context code fixes.</summary>
public static class JsonContextFix
{
    /// <summary>The fully qualified type to declare, e.g. <c>global::Shop.Payments.ChargePayment</c>.</summary>
    public const string TypeProperty = "Wyrd.JsonType";

    /// <summary>The union case name, e.g. <c>Charged</c>.</summary>
    public const string CaseProperty = "Wyrd.UnionCase";
}
