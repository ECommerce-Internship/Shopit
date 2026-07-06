# SCRUM-153: containerizes Shopit.MCP so it runs as an independent service in
# docker-compose.yml, reachable by the API over HTTP rather than being
# spawned as a child process via stdio.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Shopit.MCP/ Shopit.MCP/
COPY Shopit.Application/ Shopit.Application/
COPY Shopit.Infrastructure/ Shopit.Infrastructure/
COPY Shopit.Domain/ Shopit.Domain/

# Note: deliberately not splitting restore and publish into separate layers
# (a common Docker optimization). That split reliably failed in practice -
# "dotnet publish --no-restore" couldn't find certain build-time analyzer
# packages (e.g. Microsoft.EntityFrameworkCore.Analyzers) that a prior
# standalone "dotnet restore" had not pulled in, even on a clean --no-cache
# build with a healthy network. A single combined publish (which performs
# its own implicit restore) avoids this category of bug entirely, at the
# cost of losing the source-only-change layer-caching speedup.
RUN dotnet publish Shopit.MCP/Shopit.MCP.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is needed for docker-compose.yml's healthcheck (CMD curl -f .../health)
# - the base aspnet runtime image does not include it by default.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Shopit.MCP.dll"]