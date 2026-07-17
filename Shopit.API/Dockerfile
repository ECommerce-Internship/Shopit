# SCRUM-153: containerizes Shopit.API so the full docker-compose up flow
# (API + MCP server + Postgres + Redis + SEQ + Azurite) works end-to-end,
# matching the MCP server's own containerization in this same story.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Shopit.API/ Shopit.API/
COPY Shopit.Application/ Shopit.Application/
COPY Shopit.Infrastructure/ Shopit.Infrastructure/
COPY Shopit.Domain/ Shopit.Domain/
# SCRUM-166: the feature docs are the RAG corpus. Shopit.API.csproj pulls them in
# via a <Content Include="..\docs\features\**"> item, so they must be present in
# the build context before publish or ingestion would find an empty corpus.
COPY docs/ docs/

# See Shopit.MCP/Dockerfile for why this is a single combined publish rather
# than a separate restore + publish --no-restore split.
RUN dotnet publish Shopit.API/Shopit.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Development
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet Shopit.API.dll"]