; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
WYRD0001 | Wyrd.Messages | Error | A message is either a command or an event
WYRD0101 | Wyrd.Messages | Error | Integration events must not expose domain types
WYRD0102 | Wyrd.Messages | Error | Integration events need a name, a version and IIntegrationEvent
WYRD0200 | Wyrd.Contracts | Warning | Integration event is missing from the contract snapshot
WYRD0201 | Wyrd.Contracts | Error | Breaking change in a published integration event
WYRD0202 | Wyrd.Contracts | Warning | Contract snapshot is outdated
WYRD0203 | Wyrd.Contracts | Error | A published integration event was removed
WYRD0204 | Wyrd.Contracts | Error | Integration event name and version must be unique
