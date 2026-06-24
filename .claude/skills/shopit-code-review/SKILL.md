---
name: shopit-code-review
description: Use this skill to review Shopit code — a diff, a file, or a whole folder — against the project's own conventions. It enforces Shopit-specific rules (Clean Architecture file placement, thin controllers with [ProducesResponseType] on every status code, domain-exception error handling, FluentValidation validators, layer-first DTO placement, injected ILogger<T>, async/CancellationToken, DI registration in both hosts, xUnit test style) and produces a findings report that flags anything misplaced or out of scope. Trigger it whenever the user asks to "review", "check conventions", "code review", or audit part of the Shopit codebase.
---

# Shopit Code-Review Skill

A **project-specific** review checklist. Unlike a generic review, every rule below maps to
a convention that already exists in this codebase (see `CLAUDE.md` and the patterns it
documents). Use it to catch convention drift, misplaced files, and out-of-scope logic.

## How to use

1. **Establish scope.** Decide what you are reviewing:
   - a folder (e.g. `Shopit.Infrastructure/Repositories/`),
   - a set of changed files (`git diff --name-only main...HEAD`), or
   - a single file.
2. **Classify each file by layer** (Domain / Application / Infrastructure / API / MCP /
   Tests) and **apply only the checklist sections relevant to that layer.** A repository is
   not judged by controller rules, etc.
3. **Walk the checklist top to bottom for each file.** For every rule, record either a
   finding (with `File:Line`) or that it passed.
4. **Write the output** in the required format (see *Output format*). Do **not** modify the
   reviewed source — a review reports and recommends; it does not fix.

## Checklist

### 1. Architecture & file placement
- [ ] **Services** (`*Service` implementing an `I*Service`) live in
      `Shopit.Infrastructure/Services/`, **not** in `Repositories/` or elsewhere.
- [ ] **Repositories** (`*Repository`) live in `Shopit.Infrastructure/Repositories/`.
- [ ] **Service/repository interfaces** live in `Shopit.Application/Interfaces/`.
- [ ] The file's **`namespace` matches its physical folder** (e.g. a file in
      `Infrastructure/Services` is in `Shopit.Infrastructure.Services`).
- [ ] **Dependency direction holds**: `Domain` references nothing; `Application` references
      only `Domain` (never `Infrastructure`); `Infrastructure`/`API` may reference inward.
- [ ] **`Domain` stays pure** — no EF Core, ASP.NET, or third-party infrastructure types.

### 2. Controllers (API layer)
- [ ] Class has `[ApiController]`, `[ApiVersion("…")]`, and route
      `api/v{version:apiVersion}/...`.
- [ ] **Every action declares `[ProducesResponseType]` for every status code it can
      return** (success *and* each error path: 400/401/403/404/409/502 as applicable).
- [ ] `[Authorize]` / `[Authorize(Roles = "...")]` is present where the endpoint requires
      it (and not silently commented out).
- [ ] File-upload endpoints declare `[Consumes("multipart/form-data")]` and a size limit.
- [ ] **Controller is thin**: it validates input shape and delegates to a service. **No
      business logic** (no EF queries, no entity mapping, no domain decisions) in the
      controller.
- [ ] **No `try/catch` for domain exceptions** — they bubble to
      `ExceptionHandlingMiddleware`.

### 3. Error handling
- [ ] Services throw **domain exceptions** from `Shopit.Domain.Exceptions`
      (`NotFoundException`, `ConflictException`, `ValidationException`,
      `UnauthorizedException`, `ForbiddenException`, `ExternalServiceException`).
- [ ] Services **never** build HTTP responses, return error codes, or use `null` as an
      error signal for "not found".
- [ ] Any **new** exception type is mapped to a status in `ExceptionHandlingMiddleware`.

### 4. Validation
- [ ] Every inbound **request DTO** has a FluentValidation `AbstractValidator<T>`.
- [ ] **Shape/format** rules (required, length, range, email) live in the validator;
      **business rules** (uniqueness, stock, ownership) live in the service.
- [ ] The validator is auto-discovered (an `AddValidatorsFromAssembly` already covers its
      assembly) — no per-endpoint manual wiring.

### 5. DTO placement & mapping
- [ ] Controllers and services exchange **DTOs, never entities**; entities never cross the
      API boundary.
- [ ] DTOs follow the **layer-first** convention: `Shopit.Application/DTOs/<Feature>/`
      (the project standard; `Products/DTOs`, `AI/`, `Dashboard/` are known legacy
      exceptions — do not imitate them).
- [ ] Naming: `…Request` (inbound), `…Response` / `…Dto` (outbound),
      `…QueryParameters` (query binding); immutable DTOs are `record`s.

### 6. Logging
- [ ] Components log via an **injected `ILogger<T>`**, **not** the static `Serilog.Log`.
- [ ] Log messages use **structured properties** (`"... {Count}", n`), not string
      interpolation/concatenation.

### 7. Async & cancellation
- [ ] I/O-bound methods are `async`, suffixed `Async`, and **flow a `CancellationToken`**
      where the call chain supports it.
- [ ] **No fake-async** — a method is not `async` just to `await Task.CompletedTask` around
      synchronous work.

### 8. Dependency injection
- [ ] A new service is wired as `IFooService` (Application) + `FooService`
      (Infrastructure) and **registered in both** `Shopit.API/Program.cs` **and**
      `Shopit.MCP/Program.cs` if used by the MCP host.

### 9. Testing
- [ ] New/changed services have **xUnit** tests in `Shopit.Tests` using **EF Core
      InMemory** + **Moq** for collaborators.
- [ ] Test names follow **`Method_Scenario_ExpectedResult`**; failure paths assert the
      thrown domain exception.

## Output format

Produce a Markdown report with these sections:

1. **Scope** — what was reviewed (paths) and the checklist version.
2. **Findings table**:

   | Severity | File:Line | Rule | Finding | Suggested fix |
   |---|---|---|---|---|

   Severity = **High** (architecture/placement/security/correctness), **Medium**
   (convention drift that hurts consistency/testability), **Low** (style/minor).
3. **Misplaced / out of scope** — a dedicated subsection listing files that are in the
   wrong folder/layer or contain logic that doesn't belong there, with the recommended new
   location. (Required by the review remit.)
4. **Compliant / not flagged** — briefly note what was checked and passed. For any
   checklist category with no issues, state **"scanned, found none"** explicitly — a clean
   category must be reported, not silently omitted.

A review is complete only when every file in scope has been classified, every checklist
category has an explicit pass/fail note, and no reviewed source file was modified.
