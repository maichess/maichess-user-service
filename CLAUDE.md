# User Service

Manages player profiles and statistics. Profile creation is triggered internally by Auth via gRPC; the service also exposes a REST API for profile reads and updates.

## Contracts

- **REST:** `maichess-api-contracts/rest/users.md`
- **gRPC:** `maichess-api-contracts/protos/user-service/v1/users.proto`
- **Generated stubs:** reference `Maichess.PlatformProtos` (see `maichess-api-contracts/dotnet/`)

Implement against these contracts exactly. If a contract cannot be implemented as specified, document the blocker in `CONTRACT_NOTES.md` — do not silently deviate.

## Stack

- **Runtime:** ASP.NET (net10.0), C#, nullable enabled
- **Database:** PostgreSQL via Entity Framework Core.
- **RPC:** gRPC server (base classes from `Maichess.PlatformProtos`)

## Structure

Keep the service lightweight. Avoid unnecessary layers — no repository pattern, no service layer abstractions unless complexity clearly justifies it.

```
MaichessUserService/
  Entities/        # EF Core entity classes (mapped to DB schema, no write annotations needed)
  Data/            # DbContext — QueryTrackingBehavior.NoTracking, no write DbSets
  Grpc/            # gRPC service implementations
  Rest/            # REST controllers
  Program.cs       # Minimal startup, DI wiring
```

## Entity Framework Rules

- Set `UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)` on the `DbContext`
- Never call `SaveChanges`, `SaveChangesAsync`, `Add`, `Update`, `Remove`, or `Attach`
- Use `AsNoTracking()` on individual queries if the context default is not set
- The DB schema is owned by Auth — do not define or run migrations from this service

## Code Style

- Prefer direct, readable code over clever abstractions
- One concern per class; keep classes small
- No dead code, no commented-out blocks, no TODOs left in merged code
- Use C# records for DTOs and response models
- Validate inputs at controller/RPC boundaries; trust internal data after that
- No comments unless explaining a non-obvious algorithm. Names carry intent

## Tests

- Do not change tests to make them pass — only change tests when the requirement they cover changes.
