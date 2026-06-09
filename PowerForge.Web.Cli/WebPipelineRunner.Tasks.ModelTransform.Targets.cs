using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static ModelTransformTargetConstraints? ReadTargetConstraints(JsonElement operation)
    {
        var minTargets = GetInt(operation, "minTargets") ?? GetInt(operation, "min-targets");
        var maxTargets = GetInt(operation, "maxTargets") ?? GetInt(operation, "max-targets");
        var exactTargets = GetInt(operation, "exactTargets") ?? GetInt(operation, "exact-targets");

        if (!minTargets.HasValue && !maxTargets.HasValue && !exactTargets.HasValue)
            return null;
        if (minTargets.HasValue && minTargets.Value < 0)
            throw new InvalidOperationException("target guard minTargets must be non-negative.");
        if (maxTargets.HasValue && maxTargets.Value < 0)
            throw new InvalidOperationException("target guard maxTargets must be non-negative.");
        if (exactTargets.HasValue && exactTargets.Value < 0)
            throw new InvalidOperationException("target guard exactTargets must be non-negative.");
        if (exactTargets.HasValue && (minTargets.HasValue || maxTargets.HasValue))
            throw new InvalidOperationException("target guard exactTargets cannot be combined with minTargets/maxTargets.");
        if (minTargets.HasValue && maxTargets.HasValue && minTargets.Value > maxTargets.Value)
            throw new InvalidOperationException("target guard minTargets cannot be greater than maxTargets.");

        return new ModelTransformTargetConstraints
        {
            MinTargets = minTargets,
            MaxTargets = maxTargets,
            ExactTargets = exactTargets
        };
    }

    private static void ValidateOperationTargetConstraints(
        string operationType,
        string? path,
        int matchedTargets,
        ModelTransformTargetConstraints? targetConstraints)
    {
        if (targetConstraints is null)
            return;

        var targetPath = string.IsNullOrWhiteSpace(path) ? "$" : path;
        if (targetConstraints.ExactTargets.HasValue && matchedTargets != targetConstraints.ExactTargets.Value)
        {
            throw new InvalidOperationException(
                $"{operationType} path '{targetPath}' matched {matchedTargets} target(s), expected exactly {targetConstraints.ExactTargets.Value}.");
        }

        if (targetConstraints.MinTargets.HasValue && matchedTargets < targetConstraints.MinTargets.Value)
        {
            throw new InvalidOperationException(
                $"{operationType} path '{targetPath}' matched {matchedTargets} target(s), expected at least {targetConstraints.MinTargets.Value}.");
        }

        if (targetConstraints.MaxTargets.HasValue && matchedTargets > targetConstraints.MaxTargets.Value)
        {
            throw new InvalidOperationException(
                $"{operationType} path '{targetPath}' matched {matchedTargets} target(s), expected at most {targetConstraints.MaxTargets.Value}.");
        }
    }

    private static ModelTransformOperationCondition? ReadOperationCondition(JsonElement operation)
    {
        if (!TryGetObject(operation, out var when, "when", "where"))
            return null;

        return ParseOperationCondition(when, "when/where");
    }

    private static ModelTransformOperationCondition? ReadSourceOperationCondition(JsonElement operation)
    {
        if (!TryGetObject(
                operation,
                out var when,
                "fromWhen",
                "from-when",
                "sourceWhen",
                "source-when",
                "fromWhere",
                "from-where",
                "sourceWhere",
                "source-where"))
        {
            return null;
        }

        return ParseOperationCondition(when, "fromWhen/sourceWhen");
    }

    private static ModelTransformOperationCondition ParseOperationCondition(JsonElement when, string label)
    {
        var exists = GetBool(when, "exists") ?? GetBool(when, "present");
        var type = (GetString(when, "type") ?? GetString(when, "kind"))?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(type) &&
            !type.Equals("object", StringComparison.Ordinal) &&
            !type.Equals("array", StringComparison.Ordinal) &&
            !type.Equals("string", StringComparison.Ordinal) &&
            !type.Equals("number", StringComparison.Ordinal) &&
            !type.Equals("boolean", StringComparison.Ordinal) &&
            !type.Equals("null", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}.type '{type}' is not supported. Supported: object, array, string, number, boolean, null.");
        }

        var hasEquals = TryGetJsonValue(when, out var equalsValue, "equals", "eq");
        var hasNotEquals = TryGetJsonValue(when, out var notEqualsValue, "notEquals", "not-equals", "neq");
        if (!exists.HasValue && string.IsNullOrWhiteSpace(type) && !hasEquals && !hasNotEquals)
            throw new InvalidOperationException($"{label} requires at least one condition (exists, type, equals, notEquals).");

        return new ModelTransformOperationCondition
        {
            Exists = exists,
            Type = string.IsNullOrWhiteSpace(type) ? null : type,
            HasEquals = hasEquals,
            EqualsValue = equalsValue,
            HasNotEquals = hasNotEquals,
            NotEqualsValue = notEqualsValue
        };
    }

    private static bool EvaluateOperationCondition(JsonNode? root, string? path, ModelTransformOperationCondition? condition)
    {
        if (condition is null)
            return true;

        var exists = TryGetNodeAtPath(root, path, out var currentValue);
        if (condition.Exists.HasValue && condition.Exists.Value != exists)
            return false;

        if (!exists)
        {
            if (condition.HasEquals || !string.IsNullOrWhiteSpace(condition.Type))
                return false;
            return !condition.HasNotEquals || !JsonNode.DeepEquals(null, condition.NotEqualsValue);
        }

        if (!string.IsNullOrWhiteSpace(condition.Type))
        {
            var actualType = GetNodeTypeName(currentValue);
            if (!string.Equals(actualType, condition.Type, StringComparison.Ordinal))
                return false;
        }

        if (condition.HasEquals && !JsonNode.DeepEquals(currentValue, condition.EqualsValue))
            return false;

        if (condition.HasNotEquals && JsonNode.DeepEquals(currentValue, condition.NotEqualsValue))
            return false;

        return true;
    }

    private static bool TryGetNodeAtPath(JsonNode? root, string? path, out JsonNode? value)
    {
        value = null;
        var segments = ParsePath(path);
        if (segments.Count == 0)
        {
            if (root is null)
                return false;
            value = root;
            return true;
        }

        JsonNode? current = root;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.Kind == ModelPathSegmentKind.Property)
            {
                if (current is not JsonObject obj)
                    return false;
                if (!obj.TryGetPropertyValue(segment.Property!, out current))
                    return false;
                continue;
            }

            if (segment.Kind == ModelPathSegmentKind.Index)
            {
                if (current is not JsonArray array)
                    return false;
                if (!segment.Index.HasValue || segment.Index.Value < 0 || segment.Index.Value >= array.Count)
                    return false;
                current = array[segment.Index.Value];
                continue;
            }

            return false;
        }

        value = current;
        return true;
    }

    private static string GetNodeTypeName(JsonNode? node)
    {
        if (node is null)
            return "null";
        if (node is JsonObject)
            return "object";
        if (node is JsonArray)
            return "array";

        var raw = node.ToJsonString();
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True => "boolean",
            JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => "unknown"
        };
    }

    private static bool TryGetObject(JsonElement source, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (!source.TryGetProperty(name, out var candidate))
                continue;
            if (candidate.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"{name} must be an object.");
            value = candidate;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetJsonValue(JsonElement source, out JsonNode? value, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (!source.TryGetProperty(name, out var candidate))
                continue;

            value = JsonNode.Parse(candidate.GetRawText());
            return true;
        }

        value = null;
        return false;
    }

    private sealed class ModelTransformTargetConstraints
    {
        public int? MinTargets { get; set; }
        public int? MaxTargets { get; set; }
        public int? ExactTargets { get; set; }
    }

    private sealed class ModelTransformOperationCondition
    {
        public bool? Exists { get; set; }
        public string? Type { get; set; }
        public bool HasEquals { get; set; }
        public JsonNode? EqualsValue { get; set; }
        public bool HasNotEquals { get; set; }
        public JsonNode? NotEqualsValue { get; set; }
    }
}
