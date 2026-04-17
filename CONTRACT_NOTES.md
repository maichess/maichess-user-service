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
