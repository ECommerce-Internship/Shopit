# Code Review — `Shopit.Infrastructure/Repositories/`

**Reviewer:** `shopit-code-review` skill
**Scope:** `Shopit.Infrastructure/Repositories/` — `CategoryRepository.cs`, `CategoryService.cs`
**Date:** 2026-06-24
**Ticket:** SCRUM-86

Files reviewed against the Shopit checklist (Architecture & placement, Error handling,
Validation, DTOs, Logging, Async/cancellation, DI, Testing). Findings below; no source
files were modified.

## Findings

| Severity | File:Line | Rule | Finding | Suggested fix |
|---|---|---|---|---|
| **High** | `CategoryService.cs:7,9` | Architecture & placement | `CategoryService` is a **service** (implements `ICategoryService`) but lives in `Infrastructure/Repositories/` with namespace `Shopit.Infrastructure.Repositories`. Every other service lives in `Infrastructure/Services/`. | Move the file to `Shopit.Infrastructure/Services/`, change namespace to `Shopit.Infrastructure.Services`. No DI change needed (registered by interface). |
| **Medium** | `CategoryService.cs:1,21,30,41,62,101,114,120` | Logging | Uses the **static** `Serilog.Log.Information/Warning(...)` instead of an injected `ILogger<CategoryService>`. Inconsistent with the rest of the codebase (e.g. `SftpProductImportService` injects `ILogger<T>`) and makes the class harder to unit-test. | Inject `ILogger<CategoryService>` via the constructor and replace `Log.*` calls with `_logger.*`. Remove the `using Serilog;` import. |
| **Medium** | `CategoryService.cs:11,13` | Architecture (consistency) | `CategoryService` is the **only** service that depends on a repository abstraction (`ICategoryRepository`); the rest of the codebase injects `AppDbContext` directly. The repository pattern is applied inconsistently project-wide. | Not a defect in isolation. Pick one standard codebase-wide (either adopt repositories everywhere or drop this one) and document it in `CLAUDE.md`. Track separately. |
| **Low** | `CategoryRepository.cs:43-47` | Async & cancellation | `DeleteAsync` is `async` but only performs the synchronous `_context.Categories.Remove(category)` then `await Task.CompletedTask;` — fake-async. | Make it non-async (`public Task DeleteAsync(Category c) { _context.Categories.Remove(c); return Task.CompletedTask; }`) or have it do the actual save. |
| **Low** | `CategoryService.cs:18,25,36,66,105` · `CategoryRepository.cs:17,25,32,38,43,49,55` | Async & cancellation | Public `async` methods do not accept/flow a `CancellationToken`, so requests can't be cancelled through this path. | Add a `CancellationToken cancellationToken = default` parameter and pass it into the EF calls (and the interfaces). |
| **Low** | `CategoryRepository.cs:32-36` | Style / efficiency | `GetByNameAsync` compares `c.Name.ToLower() == name.ToLower()`, which emits SQL `LOWER()` on the column and is non-sargable (can't use an index). | Prefer a case-insensitive collation or `EF.Functions.Like` / `string.Equals(..., StringComparison.OrdinalIgnoreCase)` translated server-side. Minor. |

## Misplaced / out of scope

- **`CategoryService.cs` — service located in the repositories folder (primary finding).**
  This is the misplacement the review remit targets. `Infrastructure/Repositories/` should
  contain only repositories (data-access). `CategoryService` is application/business
  orchestration (uniqueness checks, circular-reference guards, entity→DTO mapping) and
  belongs in `Infrastructure/Services/` alongside `ProductService`, `OrderService`,
  `CategoryRepository`'s service peers, etc.
  **Action:** move the file, fix the namespace to `Shopit.Infrastructure.Services`. DI
  registration in `Shopit.API/Program.cs` and `Shopit.MCP/Program.cs` already binds by
  interface, so no registration change is required — but rebuild to confirm.

No other out-of-scope logic was found: `CategoryRepository` correctly contains only
data-access and is in the right folder.

## Compliant / not flagged

- **Architecture & placement** — `CategoryRepository.cs` is correctly named, correctly
  placed, namespace matches folder, and contains only EF data-access. ✔
- **Error handling** — `CategoryService` throws domain exceptions (`NotFoundException`,
  `ConflictException`) and never builds HTTP responses or returns null-as-error. ✔
- **Validation** — request DTOs have FluentValidation validators
  (`CreateCategoryRequestValidator`, `UpdateCategoryRequestValidator` in
  `Application/Validators/`); business rules (duplicate name, circular parent) correctly
  live in the service, not the validator. ✔
- **DTOs** — service exchanges `CategoryResponse` / `CreateCategoryRequest` /
  `UpdateCategoryRequest` (DTOs in the layer-first `Application/DTOs/Categories/`), never
  leaks `Category` entities; mapping via `MapToResponse`. ✔
- **DI** — `ICategoryService`/`CategoryService` and `ICategoryRepository`/
  `CategoryRepository` are registered in **both** `Shopit.API/Program.cs` and
  `Shopit.MCP/Program.cs`. ✔
- **Controllers** — out of scope for this folder; **scanned, found none** here.
- **Testing** — `Shopit.Tests/CategoryServiceTests.cs` exists and covers the service.
  Coverage depth not assessed in this pass.

## Summary

6 findings: **1 High** (misplaced service), **2 Medium** (static logging, inconsistent
repository pattern), **3 Low** (fake-async delete, missing `CancellationToken`,
non-sargable name lookup). The single actionable correctness/placement issue is moving
`CategoryService.cs` into `Infrastructure/Services/`.
