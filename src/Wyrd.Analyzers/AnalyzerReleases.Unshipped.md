; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
WYRD0001 | Wyrd.Messages | Error | A message is either a command or an event
WYRD0002 | Wyrd.Messages | Error | A command has exactly one owner
WYRD0003 | Wyrd.Messages | Info | Name events in the past tense
WYRD0004 | Wyrd.Messages | Info | Name commands in the imperative
WYRD0011 | Wyrd.Processes | Warning | Say how a step is undone
WYRD0012 | Wyrd.Processes | Warning | A wait needs a timeout
WYRD0013 | Wyrd.Processes | Warning | Write processes in the repository's style
WYRD0014 | Wyrd.Processes | Warning | Say how a step retries
WYRD0020 | Wyrd.Messages | Error | A switch over a union handles every case
WYRD0101 | Wyrd.Messages | Error | Integration events must not expose domain types
WYRD0102 | Wyrd.Messages | Error | Integration events need a name, a version and IIntegrationEvent
WYRD0300 | Wyrd.Json | Error | Wyrd serializes this type: declare it in the JSON context
WYRD0301 | Wyrd.Json | Error | Union cases must be declared as JSON derived types
WYRD0302 | Wyrd.Json | Error | Types in the JSON context need unique names
WYRD0200 | Wyrd.Contracts | Warning | Integration event is missing from the contract snapshot
WYRD0201 | Wyrd.Contracts | Error | Breaking change in a published integration event
WYRD0202 | Wyrd.Contracts | Warning | Contract snapshot is outdated
WYRD0203 | Wyrd.Contracts | Error | A published integration event was removed
WYRD0204 | Wyrd.Contracts | Error | Integration event name and version must be unique
