# Shopit

A full-stack e-commerce API built with ASP.NET Core 10, Entity Framework Core, and PostgreSQL.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)

## Local Development Setup

### 1. Start Docker services

```bash
docker compose up -d
```

This starts:
- **PostgreSQL 16** on port `5433`
- **Redis 7** on port `6379`
- **Azurite** (Azure Storage emulator) on ports `10000-10002`
- **Seq** (logging) on port `5341`

### 2. Configure user secrets

```bash
cd Shopit.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=ShopitDb;Username=postgres;Password=postgres;SSL Mode=Disable"
```

### 3. Apply migrations

```bash
dotnet ef database update --project Shopit.Infrastructure --startup-project Shopit.API
```

### 4. Run the API

```bash
dotnet run --project Shopit.API
```

API will be available at `https://localhost:7001/swagger`

## Database

PostgreSQL 16 running in Docker. EF Core handles migrations and seeding automatically on startup in Development mode.

## Running Tests

```bash
dotnet test
```

## Tech Stack

- ASP.NET Core 10
- Entity Framework Core 10 + Npgsql (PostgreSQL)
- Redis (caching)
- Azure Blob Storage / Azurite (local)
- Docker