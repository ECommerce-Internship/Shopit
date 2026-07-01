using System.Text.Json;
using ModelContextProtocol.Client;

// ---------------------------------------------------------------------------
// THROWAWAY SPIKE — drives the Shopit MCP server from .NET as an MCP client.
// Goal: prove we can list tools and invoke search_products. Code is disposable.
// ---------------------------------------------------------------------------

// Path to the compiled MCP server executable. Built via:
//   dotnet build Shopit.MCP\Shopit.MCP.csproj
// The exe lives next to its appsettings.json (which must have real connection
// strings), so we also set WorkingDirectory to that bin folder.
const string mcpExePath =
    @"C:\Users\maria\Shopit\Shopit.MCP\bin\Debug\net10.0\Shopit.MCP.exe";
const string mcpWorkingDir =
    @"C:\Users\maria\Shopit\Shopit.MCP\bin\Debug\net10.0";

Console.WriteLine("=== Shopit MCP client spike ===");
Console.WriteLine($"Launching server: {mcpExePath}");
Console.WriteLine($"Working dir:      {mcpWorkingDir}");
Console.WriteLine();

// 1) Define how to launch the server over stdio.
var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "Shopit.MCP",
    Command = mcpExePath,
    WorkingDirectory = mcpWorkingDir,
});

// 2) Create the client — this spawns the server process and does the
//    MCP initialize handshake. (v1.4.0: McpClient.CreateAsync, not a factory.)
Console.WriteLine("Connecting (this also waits for server startup)...");
await using var client = await McpClient.CreateAsync(transport);
Console.WriteLine("Connected.\n");

// 3) List every tool the server exposes, with its input schema.
var tools = await client.ListToolsAsync();
Console.WriteLine($"--- ListToolsAsync returned {tools.Count} tools ---\n");
foreach (var tool in tools)
{
    Console.WriteLine($"* {tool.Name}");
    if (!string.IsNullOrWhiteSpace(tool.Description))
        Console.WriteLine($"    {tool.Description}");

    // Pretty-print the JSON input schema so we can see parameter shapes.
    var schema = JsonSerializer.Serialize(
        tool.JsonSchema,
        new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine("    input schema:");
    foreach (var line in schema.Split('\n'))
        Console.WriteLine("      " + line.TrimEnd());
    Console.WriteLine();
}

// 4) Invoke search_products with a sample term and print the result.
const string sampleSearch = "a";
Console.WriteLine($"--- Calling search_products(search: \"{sampleSearch}\") ---");
var result = await client.CallToolAsync(
    "search_products",
    new Dictionary<string, object?>
    {
        ["search"] = sampleSearch,
        ["pageNumber"] = 1,
        ["pageSize"] = 5,
    });

// The tool returns its payload as text content blocks.
foreach (var block in result.Content)
{
    if (block is ModelContextProtocol.Protocol.TextContentBlock text)
        Console.WriteLine(text.Text);
    else
        Console.WriteLine($"[non-text block: {block.GetType().Name}]");
}

Console.WriteLine("\n=== Spike complete ===");