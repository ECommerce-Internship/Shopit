using System.Security.Cryptography;
using System.Text;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// A parsed slice of a feature doc, ready to be embedded and stored (SCRUM-166).
/// </summary>
/// <param name="FeatureName">The doc's top-level "#" heading.</param>
/// <param name="Section">The "##" section heading this chunk came from.</param>
/// <param name="Content">
/// The text to embed: the section body prefixed with a "Feature &gt; Section"
/// breadcrumb so the chunk is self-contained for similarity search.
/// </param>
/// <param name="ContentHash">SHA-256 of the raw source section (used to skip unchanged chunks).</param>
public record ParsedChunk(string FeatureName, string Section, string Content, string ContentHash);

/// <summary>
/// Splits a feature documentation Markdown file into one chunk per "##" section
/// (SCRUM-166). Pure and side-effect free so the chunking rules can be unit
/// tested directly.
///
/// Each chunk's embedded <see cref="ParsedChunk.Content"/> is prefixed with a
/// "Feature &gt; Section" breadcrumb, because the section bodies are short and
/// lose their subject in isolation (e.g. "Anyone. These endpoints are public.")
/// — the breadcrumb keeps the chunk self-contained so similarity search matches
/// questions that name the feature. Markdown tables are kept intact within their
/// section rather than split.
/// </summary>
public class MarkdownFeatureChunker
{
    /// <summary>
    /// Parses the given Markdown into chunks. Any text between the "#" heading and
    /// the first "##" section is emitted as an "Overview" chunk. Sections with no
    /// body are skipped.
    /// </summary>
    public IReadOnlyList<ParsedChunk> Chunk(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        var featureName = string.Empty;
        var chunks = new List<ParsedChunk>();

        string currentSection = "Overview";
        var buffer = new List<string>();
        var started = false; // whether we've opened a section buffer worth flushing

        void Flush()
        {
            if (!started)
                return;

            var body = string.Join("\n", buffer).Trim();
            if (body.Length > 0)
                chunks.Add(BuildChunk(featureName, currentSection, body));

            buffer.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                featureName = line[2..].Trim();
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                currentSection = line[3..].Trim();
                started = true;
                continue;
            }

            // Text before the first "##" (rare) accumulates under "Overview".
            if (!started && line.Trim().Length > 0)
                started = true;

            buffer.Add(line);
        }

        Flush();
        return chunks;
    }

    private static ParsedChunk BuildChunk(string featureName, string section, string body)
    {
        var breadcrumb = string.IsNullOrEmpty(featureName)
            ? section
            : $"{featureName} > {section}";

        var content = $"{breadcrumb}\n{body}";
        var hash = ComputeHash($"{breadcrumb}\n{body}");
        return new ParsedChunk(featureName, section, content, hash);
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }
}
