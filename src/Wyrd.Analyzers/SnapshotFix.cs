namespace Wyrd.Analyzers;

/// <summary>Diagnostic properties read by the snapshot code fix.</summary>
public static class SnapshotFix
{
    /// <summary>The line to write in contracts.wyrd.txt.</summary>
    public const string LineProperty = "Wyrd.ContractLine";

    /// <summary>The contract id (<c>name vN</c>) whose line must be replaced, if present.</summary>
    public const string IdProperty = "Wyrd.ContractId";
}
