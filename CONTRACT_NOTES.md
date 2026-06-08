# Contract Notes

## `user.events.v1` is produced by CDC, not by this service — shipped (feature-prompts/10)

Per [change-data-capture.md](../../maichess-knowledge-base/change-data-capture.md), the
canonical `user.events.v1` topic is **derived from the Postgres WAL by Debezium**, not emitted
in-process. The write path (gRPC/REST → `Database.DatabaseClient` → Postgres) stays
**Postgres-only**; a relay turns the raw `user.cdc.v1` change stream into the curated,
envelope-wrapped `user.events.v1`.

- **`Kafka/CdcUserEventMapper.cs`** — the pure, fully-tested transform. CDC change row →
  `UserEvent` envelope(s): `c`/`r` → `UserRegistered`; `u` → `ProfileUpdated` (username/dev_mode
  changed) and/or `RatingUpdated` (rating/rd/volatility/elo changed); `d` → nothing.
- **`Kafka/UserCdcRelay.cs`** — `[ExcludeFromCodeCoverage]` BackgroundService wiring
  consume(`user.cdc.v1`) → mapper → produce(`user.events.v1`). Gated by `Cdc:Enabled`
  (off by default; the Helm chart sets it where `kafkaConnect.enabled`).

### No dual write existed to remove

The prompt assumes a "legacy" in-process emitter that this stage replaces. **No such emitter
was ever implemented** — the event-driven rollout (event-driven-architecture.md, step 5) never
reached User. So CDC is the *sole* producer of `user.events.v1` from day one; there was nothing
to delete. The reconciliation test (`Kafka/CdcReconciliationTests.cs`) therefore compares CDC
output against a **reference** of the intended per-operation emitter, not a running one.

### REPLICA IDENTITY FULL is required (added to the user-db migration)

Faithful per-operation events need the full *before* image to tell a profile change from a
rating change. Postgres logical replication ships only the primary key in the before-image
unless the table is `REPLICA IDENTITY FULL`, so
`Adapters/Postgres/Migrations/UserPostgresMigration.cs` now sets it on `users`. The mapper still
degrades safely (emits both events for the current state) if a before-image is absent.

### Open gap for Stage 3 (feature-prompts/11) — stats not carried by `user.events`

The Redis user replica in `11` needs `{ wins, losses, draws }`, but no `user.events` payload
carries them (`RatingUpdated` has only rating fields; `ProfileUpdated` only username/dev_mode).
CDC change rows *do* see the stat columns, but there is nowhere in the **unchanged** contract to
put them. Prompt `10` deliberately keeps the schema frozen, so this is **left as a contract gap
for `11`** to close via the standard api-contracts publish/bump handoff (e.g. add
`wins`/`losses`/`draws` to `RatingUpdated`, or a full-state snapshot payload — the natural CDC
shape). Do not silently change the schema here.

**Keeps (synchronous gRPC):** `CreateUser` and `GetUser` (Auth login/register path), plus the
REST profile endpoints. `Users.RecordMatchResult` is unchanged by this stage.

---

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

## `dev_mode` contract — shipped

Feature `01-dev-mode-toggle` added `dev_mode` to the user contract:

- `User.dev_mode` (`bool`, field 7)
- `UpdateUserRequest.dev_mode` (`optional bool`, field 3)
- `dev_mode` in the `GET /users/me` response and `PATCH /users/me` request
  (`rest/users.md`)

These are published in `Maichess.PlatformProtos` and this service references the
package (currently **0.4.0**), so `User.DevMode` and
`UpdateUserRequest.HasDevMode`/`DevMode` resolve and the build succeeds. (Earlier
versions of this note tracked an unpublished `0.3.3` handoff; that is resolved.)

---

## Schema migration required for new profile fields

An earlier version of this note claimed "no data migration needed" because
persistence goes through the generic database-service (`Struct` records). That
was wrong for `user-db`, which is **PostgreSQL with typed columns**: a `Struct`
field with no backing column makes the `UPDATE`/`INSERT` fail (`42703`, undefined
column). This is exactly why the client dev-mode toggle returned "Failed to
update developer mode" — the `dev_mode` column never existed. The read-side
missing-field fallback only masked it on reads.

The `dev_mode` column (and the Glicko-2 columns `rating`, `rating_deviation`,
`volatility`, which had the same latent problem affecting registration and
match-result recording) are added in the database service's
`Adapters/Postgres/Migrations/UserPostgresMigration.cs` via idempotent
`ALTER TABLE ... ADD COLUMN IF NOT EXISTS`, with `rating` backfilled from the
existing `elo` for pre-existing rows. (The EF Core schema section near the top
of this file is legacy and does not reflect the current
`Database.DatabaseClient` persistence path.)
