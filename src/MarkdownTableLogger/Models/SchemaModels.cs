using System.Text.Json.Serialization;

namespace MarkdownTableLogger.Models;

public record ProjectResult(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("errors")] int Errors,
    [property: JsonPropertyName("warnings")] int Warnings = 0,
    [property: JsonPropertyName("succeeded")] bool Succeeded = true
);

public record ErrorDiagnostic(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("column")] int Column,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string? Message = null
);

public record EnhancedErrorDiagnostic(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("column")] int Column,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("anchor")] string? Anchor = null,
    [property: JsonPropertyName("lines")] string? Lines = null
);

public record ErrorTypeSummary(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("description")] string? Description = null
);

public record ContextAwareDiagnostic(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("context")] DiagnosticContext? Context = null
);

public record DiagnosticContext(
    [property: JsonPropertyName("before")] ContextLine[] Before,
    [property: JsonPropertyName("error")] ContextLine Error,
    [property: JsonPropertyName("after")] ContextLine[] After
);

public record ContextLine(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("text")] string Text
);

public static class ErrorCodeDescriptions
{
    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["CS0103"] = "Name does not exist in current context",
        ["CS0246"] = "Type or namespace not found", 
        ["CS1061"] = "Member does not exist",
        ["CS1501"] = "No overload takes N arguments",
        ["CS1998"] = "Async method lacks await operators",
        ["CS0029"] = "Cannot implicitly convert type",
        ["CS0534"] = "Does not implement inherited abstract member"
    };
    
    public static string? GetDescription(string code) => 
        Descriptions.TryGetValue(code, out var desc) ? desc : null;
}