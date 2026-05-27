# maichess-user-service

See `CLAUDE.md` for architecture, contracts, and design notes.

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
