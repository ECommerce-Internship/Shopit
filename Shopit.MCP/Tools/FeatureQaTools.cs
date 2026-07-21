using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Shopit.Application.Rag;

namespace Shopit.MCP.Tools;

/// <summary>
/// MCP tool exposing Shopit's feature-documentation Q&amp;A (SCRUM-166). The chat
/// model calls this whenever a user asks how a Shopit feature works; the answer
/// is grounded in the feature docs via retrieval-augmented generation, so it is
/// accurate and cites its sources rather than being guessed.
/// </summary>
[McpServerToolType]
public class FeatureQaTools
{
    private readonly IFeatureQaService _featureQaService;

    public FeatureQaTools(IFeatureQaService featureQaService)
    {
        _featureQaService = featureQaService;
    }

    [McpServerTool]
    [Description(
        "Answer a question about how a Shopit feature works (e.g. reviews, checkout, the seller " +
        "dashboard, inventory) using Shopit's own documentation. Use this for any 'how does X " +
        "work / can I / who can' question about Shopit functionality. Pass a single, " +
        "self-contained question (resolve pronouns from the conversation first). Relay the " +
        "returned answer to the user, including the 'Sources' line.")]
    public async Task<string> answer_feature_question(
        [Description("A self-contained question about how a Shopit feature works.")] string question,
        CancellationToken cancellationToken = default)
    {
        var result = await _featureQaService.AskAsync(question, cancellationToken);

        if (!result.Answered)
            return result.Answer;

        var builder = new StringBuilder(result.Answer);
        if (result.Sources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("Sources: ");
            builder.Append(string.Join("; ", result.Sources
                .Select(s => $"{s.FeatureName} ({s.SourceFile})")
                .Distinct()));
        }

        return builder.ToString();
    }
}
