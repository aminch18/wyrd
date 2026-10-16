# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Process model as data with structural equality and Mermaid rendering.
- Fluent syntax and opt-in pipe syntax (`>>`) building the same process.
- Slices as commands with a static pure `Decide` (`ICommand<TSelf, TState, TEvent, TError>`) and `IState<TSelf>`.
- Deterministic `InMemoryRuntime` with a virtual clock: retries for transient faults, compensation in reverse order,
  timeouts, duplicate detection and correlation.
- `samples/Shop`: an order fulfillment process across four vertical slices.
