# Spike: Connecting to the Shopit MCP server from C# as an MCP client

**Status:** Done — proven working. This is a throwaway spike; the `Shopit.McpSpike` console
project can be discarded once the findings here are absorbed.

**Goal:** Prove .NET code can drive the `Shopit.MCP` server programmatically (list tools,
call a tool) before any production client work.

---

## TL;DR — it works

A small console app (`Shopit.McpSpike`) launches the compiled `Shopit.MCP.exe` over stdio,
completes the MCP handshake, lists all 6 tools with their schemas, and successfully calls
`search_products`, getting real product rows back from `ShopitDb`.

---

## How to run it

1. Make sure **SQL Server** (`localhost\SQLEXPRESS`, DB `ShopitDb`) and **Redis**
   (`localhost:6379`) are running — `search_products` reads/writes both (DB for data,
   Redis for caching).
2. Build the MCP server so its exe + config are fresh:
   `dotnet build Shopit.MCP\Shopit.MCP.csproj`
3. Run the spike client:
   `dotnet run --project Shopit.McpSpike`

Expected: 6 tools printed with schemas, then a JSON page of products for `search_products("a")`.

---

## Key facts for whoever writes the production client

### Package / API
- Uses the **`ModelContextProtocol`** NuGet package **v1.4.0** (already in the solution).
  The client types actually live in the **`ModelContextProtocol.Core`** assembly that comes
  with it.
- **There is NO `McpClientFactory` in v1.4.0.** Use the static factory method
  **`McpClient.CreateAsync(transport)`** instead (namespace `ModelContextProtocol.Client`).
  This is the single biggest API surprise — older examples/docs reference `McpClientFactory`.
- `StdioClientTransport` + `StdioClientTransportOptions` are also in
  `ModelContextProtocol.Client`.
- Tool input schema is exposed on `tool.JsonSchema`; tool call results come back as
  `result.Content`, a list of content blocks — cast to
  `ModelContextProtocol.Protocol.TextContentBlock` and read `.Text`.

### Launch target (exe path + working directory)
- The server is launched as a **child process over stdio**. We point the transport at the
  compiled exe:
  `C:\Users\maria\Shopit\Shopit.MCP\bin\Debug\net10.0\Shopit.MCP.exe`
- **Set `WorkingDirectory` to that same bin folder.** The server reads `appsettings.json`
  from its working directory; if the working dir is wrong it loads empty/blank config and
  crashes (see below). Both `Shopit.MCP.exe` and `dotnet Shopit.MCP.dll` work as the command;
  the exe is simplest.

### Config the server needs at startup (the main gotcha)
`Shopit.MCP\appsettings.json` ships with **empty** connection strings:
```
"DefaultConnection": "", "Redis": "", "AzureQueue": ""
```
The server will **crash on startup** until these are filled. For the spike we copied the
working values from `Shopit.API\appsettings.json`:
- `DefaultConnection`: `Server=localhost\SQLEXPRESS;Database=ShopitDb;Trusted_Connection=True;TrustServerCertificate=True;`
- `Redis`: `localhost:6379`
- `AzureQueue`: `UseDevelopmentStorage=true`

**Important nuance:** in `Shopit.MCP\Program.cs` the **`QueueClient` is constructed eagerly**
during service registration (line ~28: `new QueueClient(config["ConnectionStrings:AzureQueue"], ...)`).
So even though `search_products` never touches the queue, a null/empty `AzureQueue` value
throws `ArgumentNullException` and kills the whole server at startup. The DB and Redis
registrations are lazy by comparison, but `search_products` does use both, so they must be
valid too.

For production this config should NOT be hardcoded/committed — use user-secrets or
environment variables (same pattern the API already uses).

### Stale-build gotcha
Editing `Shopit.MCP\appsettings.json` has no effect until you **rebuild the MCP project** —
the file is copied to `bin\Debug\net10.0\appsettings.json` at build time, and the client
launches the *built* copy. We hit exactly this: fixed the source config, but the old empty
copy was still in `bin` until a rebuild.

### Diagnosing server-side startup failures
When the server process dies during startup, the client throws
`ClientTransportClosedException` **and helpfully includes the server's stderr tail** in the
message. That's how we saw the real `ArgumentNullException`. Note the MCP server is
configured to send all logs to **stderr** (in `Program.cs`,
`LogToStandardErrorThreshold = Trace`) — this is correct, because **stdout is the MCP
protocol channel** and must not be polluted by logs.

### Startup time
First connect includes building/JIT + opening DB and Redis connections, so
`McpClient.CreateAsync` takes a noticeable moment. Fine for a spike; a production client
should use a sensible timeout/cancellation token.

---

## The 6 tools the server exposes
| Tool | Required args | Notes |
|------|---------------|-------|
| `get_dashboard_summary` | none | sales + inventory stats |
| `search_products` | none (all optional: search, categoryId, minPrice, maxPrice, pageNumber, pageSize) | returns paged products |
| `get_low_stock_products` | none | |
| `get_order` | `orderId`, `userId` | |
| `get_customer_orders` | `userId` (page, pageSize optional) | |
| `get_product` | `id` | |

## Sample successful result (`search_products("a")`)
Returned 3 real rows from `ShopitDb`: "C# Programming" (Books, $39.99), "Jeans"
(Clothing, $59.99), "Laptop" (Electronics, $999.99, stock 0) — confirming live DB access
through the MCP tool.
