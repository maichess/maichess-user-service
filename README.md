# maichess-user-service

See `CLAUDE.md` for architecture, contracts, and design notes.

## CDC relay → `user.events.v1` (feature-prompts/10)

`user.events.v1` is curated from the Debezium `user.cdc.v1` change stream rather than emitted
in-process — the write path stays Postgres-only. See `CONTRACT_NOTES.md` and
[change-data-capture.md](../../maichess-knowledge-base/change-data-capture.md).

- `Kafka/CdcUserEventMapper.cs` — pure transform (CDC change row → `UserEvent` envelopes),
  fully unit-tested (`MaichessUserService.Tests/Kafka/`), including a reconciliation harness.
- `Kafka/UserCdcRelay.cs` — `[ExcludeFromCodeCoverage]` consume→produce BackgroundService.
- Enable with `Cdc__Enabled=true` (the Helm chart sets it where `kafkaConnect.enabled`);
  `KAFKA_BOOTSTRAP` / `SCHEMA_REGISTRY_URL` default to the in-cluster services.

## Mutation Testing (Stryker.NET)

Stryker is installed as a local .NET tool. Configuration lives in
`MaichessUserService.Tests/stryker-config.json`. EF Core migration files are
excluded from mutation (they mirror the coverlet exclusions).

```powershell
# First time on a clean checkout — restore the local tool
dotnet tool restore

# Run mutation tests (from the test project directory)
cd MaichessUserService.Tests
dotnet stryker
```

After the run, open `StrykerOutput/<timestamp>/reports/mutation-report.html`
in a browser to inspect surviving mutants.

To bump the Stryker version: `dotnet tool update dotnet-stryker`.
