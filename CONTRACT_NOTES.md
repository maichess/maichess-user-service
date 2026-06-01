# Contract Notes

## AOT Disabled

`PublishAot=true` has been removed from `MaichessUserService.csproj`.

**Reason:** EF Core uses runtime reflection for query translation and model building.
`Grpc.AspNetCore` generates interceptor infrastructure via dynamic proxies at startup.
Both are fundamentally incompatible with NativeAOT compilation.

**Impact:** The published binary is a standard JIT-compiled assembly. No contract changes required.

---

## Database Schema Ownership

The user service **owns and manages the `users` table** via EF Core migrations.
Auth has read-only access to this table for credential lookups (`id`, `username`, `password_hash`).

### Schema

| Column          | Type         | Constraints              |
|-----------------|--------------|--------------------------|
| `id`            | UUID         | PK                       |
| `username`      | VARCHAR(50)  | UNIQUE NOT NULL          |
| `password_hash` | VARCHAR      | NOT NULL                 |
| `elo`           | INT          | NOT NULL DEFAULT 1200    |
| `wins`          | INT          | NOT NULL DEFAULT 0       |
| `losses`        | INT          | NOT NULL DEFAULT 0       |
| `draws`         | INT          | NOT NULL DEFAULT 0       |

### Running Migrations

Requires the SSH tunnel to be open (`ssh -N maichess-db`), then:

```bash
cd MaichessUserService
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

---

## Pending: `Maichess.PlatformProtos` 0.3.3 not yet published (dev_mode)

Feature `01-dev-mode-toggle` added `dev_mode` to the user contract:

- `User.dev_mode` (`bool`, field 7)
- `UpdateUserRequest.dev_mode` (`optional bool`, field 3)
- `dev_mode` in the `GET /users/me` response and `PATCH /users/me` request
  (`rest/users.md`)

These contract edits are committed to `maichess-api-contracts` and the package
`<Version>` was bumped to **0.3.3**, but the package is **not yet published**.

**Blocker / handoff required.** Per the contract-versioning handoff:

1. In `maichess-api-contracts`, commit, tag `v0.3.3`, and push so the C# NuGet
   and Scala packages build and become available on GitHub Packages.
2. This service's `PackageReference` is already pinned to `0.3.3`; all other
   consumers were reconciled to `0.3.3` as well
   (`maichess-analysis-service`, `maichess-database-service` (+ tests),
   `maichess-match-maker-service`, `maichess-match-manager-service`, and the
   Scala `maichess-engine-service` / `maichess-move-validator-service`
   `build.sbt`).

Until `v0.3.3` is published, `dotnet restore`/`dotnet test` for this service
cannot resolve the new generated members (`User.DevMode`,
`UpdateUserRequest.HasDevMode`/`DevMode`) and the build will fail. The code in
this service is written against those members and is ready to verify once the
package is available.

**No data migration needed.** Persistence goes through the generic
database-service (`Struct` records in the `users` collection), not the EF Core
schema described above. `dev_mode` is defaulted to `false` on create and read
back with a missing-field-defaults-to-`false` fallback, so pre-existing user
records remain valid without a schema change. (The EF Core schema section above
is legacy and does not reflect the current `Database.DatabaseClient` persistence
path.)
