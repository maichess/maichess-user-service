# Contract Notes

## Event-driven migration (Kafka) — planned

Per [event-driven-architecture.md](../../maichess-knowledge-base/event-driven-architecture.md),
this service gains an event side. Event schemas are Avro in
`maichess-api-contracts/events/v1/`.

**Becomes:**
- Produces `user.events.v1`: `UserRegistered`, `ProfileUpdated`, `RatingUpdated`.
- Rating updates move from the synchronous `Users.RecordMatchResult` gRPC to **consuming**
  `MatchResultRecorded` (emitted by Match Manager on match end), then producing `RatingUpdated`.
  The Glicko-2 computation is unchanged; only its trigger moves to an event.

**Keeps (synchronous gRPC):** `CreateUser` and `GetUser` (called by Auth on the login/register
path, which stays request/response), plus the REST profile endpoints.

**Eventually retired:** `Users.RecordMatchResult` once Match Manager emits the result event.

`user-db` remains CRUD master data (Postgres typed columns); see the schema notes below.

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
