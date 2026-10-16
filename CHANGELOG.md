# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Message taxonomy: `ICommand`, `IDomainEvent`, `IIntegrationEvent` and `[IntegrationEvent(name, Version)]`.
- `Slice<TCommand, TState, TEvent, TError>`: the single owner of a command, separate from the command contract.
- `Publication<TDomainEvent, TIntegrationEvent>`: the explicit boundary from internal facts to public contracts;
  several publications per domain event allow publishing two contract versions during a migration.
- Runtime rules: one owner per command, only integration events can be published from outside,
  integration events exposed separately from domain events (`Published`).

- Process model as data with structural equality and Mermaid rendering.
- Fluent syntax and opt-in pipe syntax (`>>`) building the same process.
- Deterministic `InMemoryRuntime` with a virtual clock: retries for transient faults, compensation in reverse order,
  timeouts, duplicate detection and correlation.
- `samples/Shop`: an order fulfillment process across four vertical slices.
