using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using IdiotProof.Operations.Dsl;
using IdiotProof.Scripting;

namespace IdiotProof.Operations;

/// <summary>
/// The parts of the agent catalog that describe corrections (IP-T526): the shape of a usage error
/// and of a script repair, and how to apply each. Every shape is a JSON Schema exported from the
/// record the code actually returns, through the serializer options the results are written with,
/// so a renamed or added property changes the published shape with it (IP-LAW-4). The prose that
/// goes with a property is its <see cref="DescriptionAttribute"/>.
/// </summary>
public static class SelfDescription
{
    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = static (context, node) =>
        {
            if (node is not JsonObject schema) return node;
            var description = context.PropertyInfo is { } property
                ? (property.AttributeProvider?.GetCustomAttributes(typeof(DescriptionAttribute), false).FirstOrDefault() as DescriptionAttribute)?.Description
                : context.TypeInfo.Type.GetCustomAttribute<DescriptionAttribute>(false)?.Description;
            if (description is not null && !schema.ContainsKey("description")) schema["description"] = description;
            return schema;
        },
    };

    /// <summary>The JSON Schema of a result payload type, as <see cref="OperationJson.Options"/> writes it (snake_case keys).</summary>
    public static JsonNode SchemaOf(Type type) => OperationJson.Options.GetJsonSchemaAsNode(type, ExporterOptions);

    /// <summary>The <c>errors</c> section: every result status, and the usage error with its corrected call.</summary>
    public static JsonObject Errors() => new()
    {
        ["envelope"] = "Every result is {status, exit_code, message, data, confirmation}. 'status' is one of the statuses below; the CLI's process exit code is 'exit_code'.",
        ["statuses"] = new JsonArray([.. Enum.GetValues<OperationStatus>().Select(s => (JsonNode)new JsonObject
        {
            ["status"] = OperationParameter.EnumWireName(s),
            ["exit_code"] = (int)s,
            ["meaning"] = typeof(OperationStatus).GetField(s.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description,
        })]),
        ["usage_error"] = new JsonObject
        {
            ["status"] = OperationParameter.EnumWireName(OperationStatus.UsageError),
            ["data_schema"] = SchemaOf(typeof(UsageErrorData)),
            ["change_kinds"] = new JsonArray([.. CallChangeKinds.All.Select(k => (JsonNode)new JsonObject { ["kind"] = k.Kind, ["meaning"] = k.Meaning })]),
            ["how_to_apply"] = new JsonArray(
                "A usage error means nothing ran. data.errors lists every problem; each names its parameter and may carry its own fix (a change).",
                "When data.fix is present it is the whole corrected call, already bound again without error. Resend it: an MCP tool call or the Hub's args request sends data.fix.args to data.fix.operation; a shell runs data.fix.command_line; the Hub's argv request sends data.fix.argv.",
                "The corrected call is never run for the caller. Resending it is a new call: every gate and confirmation applies to it as to any other.",
                "When data.fix is null, at least one problem has no single correction (a missing required value, a word where a number belongs, a date that reads two ways, a number with a comma, two equally close names). Correct those from errors[].message, this operation's parameters and value_formats; apply the per-error fix where there is one."),
        },
    };

    /// <summary>
    /// The <c>dsl</c> section: every IdiotScript diagnostic code (from <see cref="ScriptDiagnosticCodes"/>, the table
    /// the analyzer reports through), the fix and repair payloads, and the schema of the canonical strategy JSON
    /// (generated from the strict reader's allow-lists).
    /// </summary>
    public static JsonObject Dsl() => new()
    {
        ["diagnostics"] = new JsonArray([.. ScriptDiagnosticCodes.All.Select(d => (JsonNode)DslDiagnosticCode.From(d).ToJson())]),
        ["repair_schema"] = SchemaOf(typeof(DslRepairData)),
        ["how_to_apply"] = new JsonArray(
            "A diagnostic's fix is one edit to the text that was analyzed: replace 'length' characters at 'offset' (0-based) with 'replacement'. A length of 0 inserts; an empty replacement deletes.",
            "To apply several fixes yourself, apply them from the highest offset down so the earlier offsets stay valid.",
            "corrected_script is the whole script with every fix applied. It is offered only when it re-analyzes with no error and parses. Send it in place of the original; nothing was saved or changed for you.",
            "When corrected_script is null but fixes exist, the fixes do not clear every error. The codes without a fix say why in 'no_fix': those need a decision from the author.",
            "Canonical strategy JSON has no fix payload: the strict reader refuses it whole, naming the key or value. Correct it against strategy_schema."),
        ["strategy_schema"] = StrategyJson.Schema(),
    };
}
