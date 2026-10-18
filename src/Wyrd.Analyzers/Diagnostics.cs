using Microsoft.CodeAnalysis;

namespace Wyrd.Analyzers;

internal static class Diagnostics
{
    private const string Messages = "Wyrd.Messages";
    private const string Contracts = "Wyrd.Contracts";
    private const string Json = "Wyrd.Json";
    private const string Processes = "Wyrd.Processes";
    private const string HelpLink = "https://github.com/aminch18/wyrd/blob/main/docs/design/messages.md";

    public static readonly DiagnosticDescriptor CommandAndEvent = new(
        "WYRD0001",
        "A message is either a command or an event",
        "'{0}' is both a command and an event. A command is an intention that can be rejected; an event is a fact that already happened. Split it into two types.",
        Messages, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor MultipleOwners = new(
        "WYRD0002",
        "A command has exactly one owner",
        "Command '{0}' is decided by more than one slice ({1}). An intention needs exactly one owner: keep one slice, or split the command.",
        Messages, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink, customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor EventNaming = new(
        "WYRD0003",
        "Name events in the past tense",
        "Event '{0}' should be named in the past tense, as a fact that already happened (e.g. 'PaymentCharged', 'OrderShipped').",
        Messages, DiagnosticSeverity.Info, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor CommandNaming = new(
        "WYRD0004",
        "Name commands in the imperative",
        "Command '{0}' reads like an event ('{1}'). Name it as an intention in the imperative (e.g. 'ChargePayment', 'ShipOrder').",
        Messages, DiagnosticSeverity.Info, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor UndoWithoutRetry = new(
        "WYRD0010",
        "A step that can be undone needs retries",
        "Step '{0}' can be undone but has no .Retry(...). Its undo uses the same retry policy, so one transient failure would leave it undone. Add .Retry(...).",
        Processes, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor CompensationUnsaid = new(
        "WYRD0011",
        "Say how a step is undone",
        "Step '{0}' has no .Undo(...), so it will not be reverted if a later step fails. Add .Undo(...), or .NoCompensation(\"why\") if nothing needs undoing.",
        Processes, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor AwaitWithoutTimeout = new(
        "WYRD0012",
        "A wait needs a timeout",
        "Await<{0}> has no .Within(...): if the event never arrives, the process waits forever. Add .Within(...).",
        Processes, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor UnionCaseNotHandled = new(
        "WYRD0020",
        "A switch over a union handles every case",
        "The switch over union '{0}' does not handle: {1}. Handle every case explicitly, without '_', so a new case breaks the build instead of being swallowed.",
        Messages, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly SuppressionDescriptor ExhaustiveUnionSwitch = new(
        "WYRDSPR0001",
        "CS8509",
        "The switch handles every case of a [Union]: it is exhaustive, no '_' needed.");

    public static readonly DiagnosticDescriptor LeaksDomainType = new(
        "WYRD0101",
        "Integration events must not expose domain types",
        "Integration event '{0}' exposes '{1}' through '{2}'. Integration events are public contracts: use primitives or other contract types, and map from the domain in a Publication.",
        Messages, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor IncompleteIntegrationEvent = new(
        "WYRD0102",
        "Integration events need a name, a version and IIntegrationEvent",
        "'{0}' {1}. An integration event implements IIntegrationEvent and is marked with [IntegrationEvent(\"context.event-name\", Version = n)].",
        Messages, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor MissingFromJsonContext = new(
        "WYRD0300",
        "Wyrd serializes this type: declare it in the JSON context",
        "'{0}' is serialized by Wyrd but is not declared in {1}. Without it, storing or sending it fails at runtime under AOT. Add [JsonSerializable(typeof({0}))] to {1}.",
        Json, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink, customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor UnionCaseNotDerived = new(
        "WYRD0301",
        "Union cases must be declared as JSON derived types",
        "Union '{0}' stores its case '{1}' polymorphically, but '{1}' is not declared. Add [JsonDerivedType(typeof({0}.{1}), \"{1}\")] to '{0}'.",
        Json, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink, customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor JsonNameCollision = new(
        "WYRD0302",
        "Types in the JSON context need unique names",
        "'{0}' and {1} share the name '{2}' in {3}, which makes the System.Text.Json generator fail (SYSLIB1031). Declare it with [JsonSerializable(typeof({0}), TypeInfoPropertyName = \"{4}\")].",
        Json, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink, customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor NotInSnapshot = new(
        "WYRD0200",
        "Integration event is missing from the contract snapshot",
        "Integration event '{0}' v{1} is not in contracts.wyrd.txt. Review the contract, then add the line '{2}' to publish it.",
        Contracts, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor BreakingChange = new(
        "WYRD0201",
        "Breaking change in a published integration event",
        "Breaking change in integration event '{0}' v{1}: {2}. Existing subscribers would fail to read it. Publish it as v{3} and keep v{1} until subscribers migrate, or revert the change.",
        Contracts, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor SnapshotOutdated = new(
        "WYRD0202",
        "Contract snapshot is outdated",
        "Integration event '{0}' v{1} gained optional fields ({2}). This is compatible: update its line in contracts.wyrd.txt to '{3}'.",
        Contracts, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor ContractRemoved = new(
        "WYRD0203",
        "A published integration event was removed",
        "Integration event '{0}' v{1} is in contracts.wyrd.txt but no longer exists. Removing a published contract breaks its subscribers. Restore it, or retire it explicitly by prefixing its line with 'retired '.",
        Contracts, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink, customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor DuplicateContract = new(
        "WYRD0204",
        "Integration event name and version must be unique",
        "Integration event '{0}' v{1} is declared by more than one type ({2}). Each name and version identifies exactly one contract.",
        Contracts, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink, customTags: WellKnownDiagnosticTags.CompilationEnd);
}
