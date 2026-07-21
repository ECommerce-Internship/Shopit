# CLAUDE.md

Guidance for Claude Code (and humans) working in the **Shopit** repository. Read this
before making changes — it documents how to build/test/run, the architecture rules that
must not be violated, and the mandatory code patterns this codebase follows.

## Overview

Shopit is an e-commerce backend built with **.NET 10 / C#** following **Clean
Architecture**. The solution (`Shopit.slnx`) contains six projects:

| Project | Type | Responsibility |
|---|---|---|
| `Shopit.Domain` | classlib | Entities, enums, domain exceptions. **Zero dependencies.** |
| `Shopit.Application` | classlib | Service interfaces, DTOs, validators, business contracts. |
| `Shopit.Infrastructure` | classlib | EF Core, repositories, service implementations, EF migrations, external integrations (SQL Server, Redis, Azure Blob/Queue, Brevo, SFTP). |
| `Shopit.API` | ASP.NET Core Web API | HTTP host — controllers, middleware, DI composition root, auth. |
| `Shopit.MCP` | console (Exe) | A **second host** exposing selected services as Model Context Protocol tools. |
| `Shopit.Tests` | xUnit | Unit/integration tests for Application + Infrastructure services. |

## Build, Test, Run

All commands run from the repository root (`C:\Aspire\Shopit`). The shell is PowerShell.

### Build
```powershell
dotnet build Shopit.slnx
```

### Test
```powershell
dotnet test                       # run the whole suite
dotnet test --filter "FullyQualifiedName~ProductServiceTests"   # one class
```

### Run the API
```powershell
dotnet run --project Shopit.API --launch-profile https
```
- HTTPS: `https://localhost:7033`  •  HTTP: `http://localhost:5129`
- Swagger UI (Development only): `https://localhost:7033/swagger`
- In **Development**, startup auto-applies EF migrations and seeds the database
  (`Shopit.API/Program.cs` — `context.Database.Migrate()` + `DbInitializer.Seed`).

### Run the MCP server
```powershell
dotnet run --project Shopit.MCP
```

### Configuration & secrets
- `appsettings.json` is committed and must contain **no real secrets** (Google,
  Brevo, SFTP, etc. are blank placeholders).
- Put local secrets in **user secrets** (`Shopit.API` has a `UserSecretsId`) or in
  `appsettings.Development.json` (gitignored). Example:
  ```powershell
  dotnet user-secrets set "Authentication:Google:ClientId" "..." --project Shopit.API
  ```
- A template of the Development settings lives in `appsettings.Development.example.json`.

## Database & EF Migrations

- Provider: **SQL Server** (default connection points at `localhost\SQLEXPRESS`,
  database `ShopitDb`). SQL Server is **not** in Docker — use a local SQL Express/LocalDB
  instance, or override `ConnectionStrings:DefaultConnection`.
- `DbContext`: `Shopit.Infrastructure/Data/AppDbContext.cs`.
- Migrations live in `Shopit.Infrastructure/Migrations/`.
- A design-time factory (`AppDbContextFactory`) reads the connection string from
  `../Shopit.API/appsettings.json`, so EF tools work without a running app.

Migrations are owned by **Infrastructure**, hosted by **API**:
```powershell
# Add a migration
dotnet ef migrations add <Name> --project Shopit.Infrastructure --startup-project Shopit.API

# Apply to the database (not needed in Dev — startup auto-migrates)
dotnet ef database update --project Shopit.Infrastructure --startup-project Shopit.API

# Roll back to a previous migration
dotnet ef database update <PreviousMigrationName> --project Shopit.Infrastructure --startup-project Shopit.API
```
> Requires the EF tools: `dotnet tool install --global dotnet-ef`.

## Docker (local dependencies)

`docker-compose.yml` provides the backing services (not the app itself). Run from the
repo root:

```powershell
docker compose up -d                 # redis, redisinsight, seq, azurite
docker compose --profile dev up -d   # also starts the SFTP server (dev profile)
docker compose down                  # stop everything
```

| Service | Container | Port(s) | Purpose |
|---|---|---|---|
| Redis | `redis` | 6379 | Caching / distributed cache |
| RedisInsight | `redisinsight` | 5540 | Redis GUI |
| Seq | `seq` | 5341 (UI) | Structured log sink (Serilog). Admin pw: `Admin@123` |
| Azurite | `azurite` | 10000–10002 | Local Azure Blob/Queue/Table emulator |
| SFTP | `sftp` | 2222 | `atmoz/sftp` test server (`testuser`/`testpass`). **`dev` profile only.** Mounts `./sftp-files` → `/upload`. |

The SFTP server backs the product import feature
(`POST /api/v{version}/products/import-from-sftp`); drop an `.xlsx` into `./sftp-files`
to test it.

## Project Dependency Rules (MUST follow)

This is Clean Architecture — **dependencies point inward only**. Enforced by the
`ProjectReference` graph; do not add references that violate it.

```
Domain          →  (nothing)
Application      →  Domain
Infrastructure   →  Application, Domain
API              →  Application, Infrastructure
MCP              →  Application, Infrastructure, Domain   (alternate composition root)
Tests            →  all of the above
```

Hard rules:
- **`Domain` references nothing** — no EF, no ASP.NET, no third-party infra. Keep it pure.
- **`Application` defines interfaces; `Infrastructure` implements them.** Application
  must never reference Infrastructure. A service contract (`IProductService`,
  `IOrderService`, …) lives in Application; its implementation lives in
  `Infrastructure/Services`.
- **`API` is the composition root** — controllers depend on Application interfaces, never
  on Infrastructure concretes directly. DI wiring lives in `Shopit.API/Program.cs`.
- **`MCP` is a parallel host** that reuses Application + Infrastructure. If you register a
  new service in `Program.cs`, check whether `Shopit.MCP/Program.cs` needs the same
  registration.

## Mandatory Patterns

### Error handling
- Business/service code throws **domain exceptions** from
  `Shopit.Domain/Exceptions/` — never returns error codes or null-as-error, and never
  builds HTTP responses itself:
  `NotFoundException` (404), `ConflictException` (409), `ValidationException` (400),
  `UnauthorizedException` (401), `ForbiddenException` (403),
  `ExternalServiceException` (502).
- `ExceptionHandlingMiddleware` (`Shopit.API/Middleware`) is the **single** translation
  point: it maps each exception to an RFC-7807 `ProblemDetails` response
  (`application/problem+json`). Unmapped exceptions become a generic 500 with a safe
  message (details are logged, not returned).
- **Controllers do not contain `try/catch` for these.** Let exceptions bubble to the
  middleware. To add a new error category: add a `*Exception` in `Shopit.Domain`, then
  add its `=> (status, title)` arm in `ExceptionHandlingMiddleware`.

### Validation
- Input validation uses **FluentValidation**. Each request DTO has an
  `AbstractValidator<T>` (e.g. `RegisterRequestValidator`).
- Validators are auto-discovered and run via `AddFluentValidationAutoValidation()` +
  `AddValidatorsFromAssembly(...)` in `Program.cs`. Add a validator and it is picked up
  automatically — no manual wiring per endpoint.
- Validation = shape/format of input (required, length, range, email). Business rules
  (duplicate SKU, insufficient stock) belong in the service and throw domain exceptions.

### DTO placement
- Controllers and services exchange **DTOs**, never EF entities. Entities
  (`Shopit.Domain/Entities`) never cross the API boundary.
- DTOs live in **`Shopit.Application`** (the contract layer), grouped by feature.
- Naming convention: `...Request` (inbound), `...Response` / `...Dto` (outbound),
  `...QueryParameters` (query-string binding). Use `record` for immutable DTOs.
- See the standardized folder convention in the next section.

### Testing style
- **xUnit** + **FluentAssertions** (`result.Should()...`) + **Moq** for collaborators.
- Service tests run against **EF Core InMemory** (`UseInMemoryDatabase($"...-{Guid}")`)
  — a fresh, uniquely-named database per test for isolation. External collaborators
  (cache, blob, Gemini, email) are mocked.
- Test method naming: **`Method_Scenario_ExpectedResult`**, e.g.
  `CreateProduct_DuplicateSKU_ThrowsConflictException`. One behavior per `[Fact]`;
  use `[Theory]`/`[InlineData]` for variants.
- Assert on thrown domain exceptions for the failure paths
  (`await act.Should().ThrowAsync<ConflictException>()`).

## Documented Inconsistency & Chosen Standard

**Inconsistency — DTO/validator folder layout.** The codebase mixes two organizational
styles inside `Shopit.Application`:

1. **Layer-first (majority):** a central `DTOs/<Feature>/` tree and a central
   `Validators/` folder — e.g. `DTOs/Auth/`, `DTOs/Categories/`, `DTOs/Payments/`,
   `DTOs/Reviews/`, and `Validators/RegisterRequestValidator.cs`.
2. **Feature-first (the exceptions):** the Products module is a vertical slice
   (`Products/DTOs/`, `Products/Validators/`), and `AI/` and `Dashboard/` keep their
   DTOs loose in the feature folder with no `DTOs` subfolder at all.

**Chosen standard going forward:** use the **layer-first** convention —
`Shopit.Application/DTOs/<Feature>/` for DTOs and `Shopit.Application/Validators/` for
validators — because it is the established majority pattern.

- New DTOs/validators **must** follow the layer-first layout.
- `Products/DTOs`, `Products/Validators`, `AI/`, and `Dashboard/` are **legacy
  exceptions**. Do not add new code imitating them; migrate opportunistically when
  touching those areas (update namespaces and the `AddValidatorsFromAssembly` /
  `IncludeXmlComments` references accordingly).

## Key Conventions Cheat-Sheet

- **Async everywhere** for I/O; suffix methods `...Async` and flow `CancellationToken`.
- **API versioning** is mandatory: routes are `api/v{version:apiVersion}/...` and
  controllers carry `[ApiVersion("1.0")]`.
- **Auth:** JWT bearer for APIs (`[Authorize]`, `[Authorize(Roles = "Admin")]`); Google
  OAuth + cookie scheme for the external-login handshake.
- **Logging:** Serilog → Console + Seq. Log with structured properties
  (`_logger.LogInformation("... {Bytes} bytes", n)`), not string interpolation.
- **Secrets:** never commit real credentials; keep `appsettings.json` placeholders blank.
- When adding a service: define `IFooService` in Application → implement `FooService` in
  Infrastructure → register in **both** `Shopit.API/Program.cs` and (if used there)
  `Shopit.MCP/Program.cs`.
