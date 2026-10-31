using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Core.Hosting;
using IdiotProof.Operations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace IdiotProof.Mcp;

/// <summary>
/// An MCP tool that forwards its call to the Hub as one operation. The tool list is generated, so
/// adding an <c>[Operation]</c> anywhere in the Hub adds a tool here with no MCP code at all.
/// </summary>
public sealed class HubTool(ToolSpec spec, HubInvoker hub) : McpServerTool
{
    public ToolSpec Spec => spec;

    public override Tool ProtocolTool { get; } = new()
    {
        Name = spec.ToolName,
        Description = spec.Description,
        InputSchema = spec.InputSchema,
        Annotations = new ToolAnnotations
        {
            Title = spec.Operation,
            ReadOnlyHint = spec.ReadOnly,
            DestructiveHint = spec.Destructive,
            IdempotentHint = spec.ReadOnly,
            OpenWorldHint = spec.Risk == "money",
        },
    };

    public override IReadOnlyList<object> Metadata { get; } = [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var args = new JsonObject();
        string? confirmation = null;
        if (request.Params?.Arguments is { } supplied)
        {
            foreach (var (key, value) in supplied)
            {
                if (key == ToolSpec.ConfirmationProperty)
                {
                    confirmation = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                    continue;
                }
                args[key] = JsonNode.Parse(value.GetRawText());
            }
        }

        var outcome = await hub.InvokeAsync(new HubOperationRequest
        {
            Operation = spec.Operation,
            Args = JsonSerializer.SerializeToElement(args),
            Caller = OperationCaller.Mcp,
            Output = OutputMode.Human,
            ConfirmationCode = confirmation,
        }, cancellationToken);

        // The Hub logs every operation it runs; what it can't see is a call that never reached it.
        if (outcome.Response is null)
            IdiotProofLog.Error("HubTool", null, "{Tool:l} did not reach the Hub: {Code:l}: {Message:l}",
                spec.ToolName, outcome.ErrorCode, outcome.ErrorMessage);

        return ToResult(outcome);
    }

    /// <summary>
    /// Human text as the content (what the assistant reads and relays), the structured envelope
    /// alongside. Failures, usage errors and gate vetoes are errors; a confirmation request is not
    /// — it is a question for the user.
    /// </summary>
    internal static CallToolResult ToResult(HubInvocationOutcome outcome)
    {
        if (outcome.Response is not { } response)
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"{outcome.ErrorCode}: {outcome.ErrorMessage}" }],
            };

        var text = response.ExitCode == 4
            ? response.Text + "\n\n[confirmation required] Show the preview above to the user. Call this tool again with the same " +
              $"arguments plus {ToolSpec.ConfirmationProperty} ONLY if they explicitly approve."
            : response.Text;

        return new CallToolResult
        {
            IsError = response.ExitCode is 1 or 2 or 3,
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = response.Envelope,
        };
    }
}
