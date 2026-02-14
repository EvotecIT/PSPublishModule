using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteModelTransform(
        JsonElement step,
        string baseDir,
        WebPipelineStepResult stepResult)
    {
        var inputPath = ResolvePath(baseDir,
            GetString(step, "input") ??
            GetString(step, "inputPath") ??
            GetString(step, "input-path") ??
            GetString(step, "source") ??
            GetString(step, "sourcePath") ??
            GetString(step, "source-path"));
        var outputPath = ResolvePath(baseDir,
            GetString(step, "out") ??
            GetString(step, "output") ??
            GetString(step, "outputPath") ??
            GetString(step, "output-path") ??
            GetString(step, "destination") ??
            GetString(step, "dest"));
        var reportPath = ResolvePath(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path"));
        var strict = GetBool(step, "strict") ?? true;
        var pretty = GetBool(step, "pretty") ?? true;
        var validateJson = GetBool(step, "validateJson") ?? GetBool(step, "validate-json") ?? true;

        if (string.IsNullOrWhiteSpace(inputPath))
            throw new InvalidOperationException("model-transform requires input.");
        if (!File.Exists(inputPath))
            throw new InvalidOperationException($"model-transform input file not found: {inputPath}");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("model-transform requires out/output path.");

        var operations = GetArrayOfObjects(step, "operations") ??
                         GetArrayOfObjects(step, "ops") ??
                         GetArrayOfObjects(step, "transforms");
        if (operations is not { Length: > 0 })
            throw new InvalidOperationException("model-transform requires operations.");

        var inputJson = File.ReadAllText(inputPath);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(inputJson);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"model-transform input is not valid JSON: {ex.Message}", ex);
        }

        var operationReports = new List<ModelTransformOperationReport>();
        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];
            var operationType = (GetString(operation, "op") ?? GetString(operation, "type"))?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(operationType))
                throw new InvalidOperationException($"model-transform operations[{index}] requires op/type.");

            try
            {
                var applyResult = ApplyModelTransformOperation(root, operationType, operation, strict);
                root = applyResult.Root;
                operationReports.Add(new ModelTransformOperationReport
                {
                    Index = index,
                    Operation = operationType,
                    Path = GetString(operation, "path") ?? GetString(operation, "target"),
                    TargetsApplied = applyResult.TargetsApplied,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                if (strict)
                {
                    throw new InvalidOperationException(
                        $"model-transform operations[{index}] '{operationType}' failed: {ex.Message}",
                        ex);
                }

                operationReports.Add(new ModelTransformOperationReport
                {
                    Index = index,
                    Operation = operationType,
                    Path = GetString(operation, "path") ?? GetString(operation, "target"),
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        var outputJson = SerializeModelTransformOutput(root, pretty);
        if (validateJson)
        {
            try
            {
                using var _ = JsonDocument.Parse(outputJson);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"model-transform output JSON validation failed: {ex.Message}", ex);
            }
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var beforeOutput = File.Exists(outputPath) ? File.ReadAllText(outputPath) : null;
        File.WriteAllText(outputPath, outputJson, Encoding.UTF8);
        var changed = !string.Equals(beforeOutput, outputJson, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            WriteModelTransformReport(reportPath, new ModelTransformReport
            {
                Input = Path.GetFullPath(inputPath),
                Output = Path.GetFullPath(outputPath),
                Strict = strict,
                Pretty = pretty,
                ValidateJson = validateJson,
                OperationsTotal = operations.Length,
                OperationsSucceeded = operationReports.Count(static op => op.Success),
                OperationsFailed = operationReports.Count(static op => !op.Success),
                Changed = changed,
                Utc = DateTimeOffset.UtcNow.ToString("O"),
                Operations = operationReports
            });
        }

        stepResult.Success = true;
        stepResult.Message = changed
            ? $"model-transform ok: applied {operations.Length} operation(s), updated '{outputPath}'."
            : $"model-transform ok: applied {operations.Length} operation(s), no output changes for '{outputPath}'.";
    }

    private static ModelTransformApplyResult ApplyModelTransformOperation(JsonNode? root, string operationType, JsonElement operation, bool strict)
    {
        var targetConstraints = ReadTargetConstraints(operation);
        var operationCondition = ReadOperationCondition(operation);
        switch (operationType)
        {
            case "set":
            {
                var path = GetString(operation, "path") ?? GetString(operation, "target");
                if (!TryGetOperationValue(operation, out var value, "value", "with"))
                    throw new InvalidOperationException("set operation requires value/with.");
                return ApplyPathOperation(root, path, static (currentRoot, targetPath, _, localValue, __, localStrict) =>
                    SetNodeAtPath(currentRoot, targetPath, localValue, localStrict), strict, value, null, "set", targetConstraints, operationCondition);
            }
            case "replace":
            {
                var path = GetString(operation, "path") ?? GetString(operation, "target");
                if (!TryGetOperationValue(operation, out var value, "value", "with"))
                    throw new InvalidOperationException("replace operation requires value/with.");
                return ApplyPathOperation(root, path, static (currentRoot, targetPath, _, localValue, __, ___) =>
                    ReplaceNodeAtPath(currentRoot, targetPath, localValue), strict, value, null, "replace", targetConstraints, operationCondition);
            }
            case "remove":
            {
                var path = GetString(operation, "path") ?? GetString(operation, "target");
                return ApplyPathOperation(root, path, static (currentRoot, targetPath, _, __, ___, ____) =>
                    RemoveNodeAtPath(currentRoot, targetPath), strict, null, null, "remove", targetConstraints, operationCondition);
            }
            case "insert":
            {
                var path = GetString(operation, "path") ?? GetString(operation, "target");
                var index = GetInt(operation, "index") ?? GetInt(operation, "at");
                if (!index.HasValue || index.Value < 0)
                    throw new InvalidOperationException("insert operation requires non-negative index/at.");
                if (!TryGetOperationValue(operation, out var value, "value", "item"))
                    throw new InvalidOperationException("insert operation requires value/item.");
                return ApplyPathOperation(root, path, static (currentRoot, targetPath, localIndex, localValue, __, ___) =>
                    InsertNodeAtPath(currentRoot, targetPath, localIndex!.Value, localValue), strict, value, index, "insert", targetConstraints, operationCondition);
            }
            case "append":
            {
                var path = GetString(operation, "path") ?? GetString(operation, "target");
                if (!TryGetOperationValue(operation, out var value, "value", "item"))
                    throw new InvalidOperationException("append operation requires value/item.");
                return ApplyPathOperation(root, path, static (currentRoot, targetPath, _, localValue, __, localStrict) =>
                    AppendNodeAtPath(currentRoot, targetPath, localValue, localStrict), strict, value, null, "append", targetConstraints, operationCondition);
            }
            case "merge":
            {
                var path = GetString(operation, "path") ?? GetString(operation, "target");
                if (!TryGetOperationValue(operation, out var value, "value", "with"))
                    throw new InvalidOperationException("merge operation requires value/with.");
                if (value is not JsonObject mergeObject)
                    throw new InvalidOperationException("merge operation value must be an object.");
                return ApplyPathOperation(root, path, static (currentRoot, targetPath, _, localValue, __, localStrict) =>
                    MergeObjectAtPath(currentRoot, targetPath, (JsonObject)localValue!, localStrict), strict, mergeObject, null, "merge", targetConstraints, operationCondition);
            }
            case "copy":
            {
                var path = GetString(operation, "path") ?? GetString(operation, "target");
                var from = GetString(operation, "from") ?? GetString(operation, "source");
                if (string.IsNullOrWhiteSpace(from))
                    throw new InvalidOperationException("copy operation requires from/source.");
                var sourceCondition = ReadSourceOperationCondition(operation);
                return ApplyTransferOperation(root, path, from, strict, removeSource: false, targetConstraints, operationCondition, sourceCondition);
            }
            case "move":
            {
                var path = GetString(operation, "path") ?? GetString(operation, "target");
                var from = GetString(operation, "from") ?? GetString(operation, "source");
                if (string.IsNullOrWhiteSpace(from))
                    throw new InvalidOperationException("move operation requires from/source.");
                var sourceCondition = ReadSourceOperationCondition(operation);
                return ApplyTransferOperation(root, path, from, strict, removeSource: true, targetConstraints, operationCondition, sourceCondition);
            }
            default:
                throw new InvalidOperationException(
                    $"model-transform operation '{operationType}' is not supported. Supported operations: set, replace, insert, remove, append, merge, copy, move.");
        }
    }

    private static JsonNode? SetNodeAtPath(JsonNode? root, string? path, JsonNode? value, bool strict)
    {
        var segments = ParsePath(path);
        if (segments.Count == 0)
            return CloneNode(value);

        root ??= segments[0].Kind == ModelPathSegmentKind.Index ? new JsonArray() : new JsonObject();
        var current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            current = GetOrCreateChildNode(current, segments[i], segments[i + 1], strict);
        }

        SetChildNode(current, segments[^1], CloneNode(value));
        return root;
    }

    private static JsonNode? ReplaceNodeAtPath(JsonNode? root, string? path, JsonNode? value)
    {
        var segments = ParsePath(path);
        if (segments.Count == 0)
        {
            if (root is null)
                throw new InvalidOperationException("replace target '$' does not exist.");
            return CloneNode(value);
        }

        if (root is null)
            throw new InvalidOperationException($"replace path '{path}' does not exist.");

        var current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var next = TryGetChildNode(current, segments[i]);
            if (next is null)
                throw new InvalidOperationException($"replace path '{path}' does not exist.");
            current = next;
        }

        var existing = TryGetChildNode(current, segments[^1]);
        if (existing is null)
            throw new InvalidOperationException($"replace path '{path}' does not exist.");

        SetChildNode(current, segments[^1], CloneNode(value));
        return root;
    }

    private static JsonNode? RemoveNodeAtPath(JsonNode? root, string? path)
    {
        var segments = ParsePath(path);
        if (segments.Count == 0)
            throw new InvalidOperationException("remove cannot target root. Provide a path.");
        if (root is null)
            throw new InvalidOperationException("remove target does not exist.");

        var current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var next = TryGetChildNode(current, segments[i]);
            if (next is null)
                throw new InvalidOperationException($"remove path '{path}' does not exist.");

            current = next;
        }

        RemoveChildNode(current, segments[^1]);
        return root;
    }

    private static JsonNode? InsertNodeAtPath(JsonNode? root, string? path, int index, JsonNode? value)
    {
        var target = GetNodeAtPath(root, path);
        if (target is not JsonArray array)
            throw new InvalidOperationException($"insert target path '{path}' must resolve to an array.");
        if (index < 0 || index > array.Count)
            throw new InvalidOperationException($"insert index {index} is out of range for '{path}' (count {array.Count}).");

        array.Insert(index, CloneNode(value));
        return ReplaceNodeAtPath(root, path, array);
    }

    private static JsonNode? GetNodeAtPath(JsonNode? root, string? path)
    {
        var segments = ParsePath(path);
        if (segments.Count == 0)
        {
            if (root is null)
                throw new InvalidOperationException("path '$' does not exist.");
            return CloneNode(root);
        }

        if (root is null)
            throw new InvalidOperationException($"path '{path}' does not exist.");

        var current = root;
        for (var i = 0; i < segments.Count; i++)
        {
            var next = TryGetChildNode(current, segments[i]);
            if (next is null)
                throw new InvalidOperationException($"path '{path}' does not exist.");
            current = next;
        }

        return CloneNode(current);
    }

    private static JsonNode? AppendNodeAtPath(JsonNode? root, string? path, JsonNode? value, bool strict)
    {
        var segments = ParsePath(path);
        if (segments.Count == 0)
        {
            if (root is not JsonArray rootArray)
            {
                if (strict)
                    throw new InvalidOperationException("append root target must be an array.");
                return root;
            }

            rootArray.Add(CloneNode(value));
            return root;
        }

        root ??= segments[0].Kind == ModelPathSegmentKind.Index ? new JsonArray() : new JsonObject();
        var target = GetOrCreateNodeAtPath(root, segments, ModelPathSegmentKind.Property, strict, createLeafAsArray: true);
        if (target is not JsonArray array)
            throw new InvalidOperationException($"append target path '{path}' must resolve to an array.");

        array.Add(CloneNode(value));
        return root;
    }

    private static JsonNode? MergeObjectAtPath(JsonNode? root, string? path, JsonObject value, bool strict)
    {
        var segments = ParsePath(path);
        if (segments.Count == 0)
        {
            if (root is not JsonObject rootObject)
            {
                if (strict)
                    throw new InvalidOperationException("merge root target must be an object.");
                return root;
            }

            foreach (var pair in value)
                rootObject[pair.Key] = CloneNode(pair.Value);
            return root;
        }

        root ??= segments[0].Kind == ModelPathSegmentKind.Index ? new JsonArray() : new JsonObject();
        var target = GetOrCreateNodeAtPath(root, segments, ModelPathSegmentKind.Property, strict, createLeafAsArray: false);
        if (target is not JsonObject objectTarget)
            throw new InvalidOperationException($"merge target path '{path}' must resolve to an object.");

        foreach (var pair in value)
            objectTarget[pair.Key] = CloneNode(pair.Value);

        return root;
    }

    private static JsonNode? GetOrCreateNodeAtPath(
        JsonNode root,
        List<ModelPathSegment> segments,
        ModelPathSegmentKind defaultLeafKind,
        bool strict,
        bool createLeafAsArray)
    {
        var current = root;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var hasNext = i < segments.Count - 1;
            var next = hasNext
                ? segments[i + 1]
                : new ModelPathSegment
                {
                    Kind = createLeafAsArray ? ModelPathSegmentKind.Index : defaultLeafKind,
                    Index = createLeafAsArray ? 0 : null
                };

            if (hasNext)
            {
                current = GetOrCreateChildNode(current, segment, next, strict);
                continue;
            }

            var existingLeaf = TryGetChildNode(current, segment);
            if (existingLeaf is not null)
                return existingLeaf;

            JsonNode leaf = createLeafAsArray ? new JsonArray() : new JsonObject();
            SetChildNode(current, segment, leaf);
            return leaf;
        }

        return current;
    }

    private static JsonNode GetOrCreateChildNode(JsonNode current, ModelPathSegment segment, ModelPathSegment nextSegment, bool strict)
    {
        var existing = TryGetChildNode(current, segment);
        if (existing is not null)
            return existing;

        JsonNode created = nextSegment.Kind == ModelPathSegmentKind.Index
            ? new JsonArray()
            : new JsonObject();
        SetChildNode(current, segment, created);
        return created;
    }

    private static JsonNode? TryGetChildNode(JsonNode current, ModelPathSegment segment)
    {
        if (segment.Kind == ModelPathSegmentKind.Wildcard || segment.Kind == ModelPathSegmentKind.RecursiveWildcard)
            throw new InvalidOperationException("Wildcard segments must be resolved before node traversal.");

        if (segment.Kind == ModelPathSegmentKind.Property)
        {
            if (current is not JsonObject obj)
                return null;
            return obj[segment.Property!];
        }

        if (current is not JsonArray array)
            return null;
        if (!segment.Index.HasValue || segment.Index.Value < 0 || segment.Index.Value >= array.Count)
            return null;
        return array[segment.Index.Value];
    }

    private static void SetChildNode(JsonNode current, ModelPathSegment segment, JsonNode? value)
    {
        if (segment.Kind == ModelPathSegmentKind.Wildcard || segment.Kind == ModelPathSegmentKind.RecursiveWildcard)
            throw new InvalidOperationException("Wildcard segments cannot be used as assignment targets.");

        if (segment.Kind == ModelPathSegmentKind.Property)
        {
            if (current is not JsonObject obj)
                throw new InvalidOperationException("path targets object property but current node is not an object.");
            obj[segment.Property!] = value;
            return;
        }

        if (current is not JsonArray array)
            throw new InvalidOperationException("path targets array index but current node is not an array.");
        if (!segment.Index.HasValue || segment.Index.Value < 0)
            throw new InvalidOperationException("path targets invalid array index.");

        EnsureArraySize(array, segment.Index.Value);
        array[segment.Index.Value] = value;
    }

    private static void RemoveChildNode(JsonNode current, ModelPathSegment segment)
    {
        if (segment.Kind == ModelPathSegmentKind.Wildcard || segment.Kind == ModelPathSegmentKind.RecursiveWildcard)
            throw new InvalidOperationException("Wildcard segments cannot be used as remove targets.");

        if (segment.Kind == ModelPathSegmentKind.Property)
        {
            if (current is not JsonObject obj)
                throw new InvalidOperationException("remove path targets object property but current node is not an object.");

            if (!obj.Remove(segment.Property!))
                throw new InvalidOperationException($"remove path property '{segment.Property}' does not exist.");

            return;
        }

        if (current is not JsonArray array)
            throw new InvalidOperationException("remove path targets array index but current node is not an array.");

        if (!segment.Index.HasValue || segment.Index.Value < 0 || segment.Index.Value >= array.Count)
            throw new InvalidOperationException("remove path array index is out of range.");

        array.RemoveAt(segment.Index.Value);
    }

    private static void EnsureArraySize(JsonArray array, int index)
    {
        while (array.Count <= index)
            array.Add(null);
    }

    private static bool TryGetOperationValue(JsonElement operation, out JsonNode? value, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (!operation.TryGetProperty(name, out var element))
                continue;

            value = JsonNode.Parse(element.GetRawText());
            return true;
        }

        value = null;
        return false;
    }

    private static List<ModelPathSegment> ParsePath(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "$")
            return new List<ModelPathSegment>();
        if (trimmed.StartsWith("$", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(1);
            if (trimmed.StartsWith(".", StringComparison.Ordinal))
                trimmed = trimmed.Substring(1);
        }

        var segments = new List<ModelPathSegment>();
        var property = new StringBuilder();
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '.')
            {
                FlushPropertySegment(property, segments);
                continue;
            }

            if (c == '[')
            {
                FlushPropertySegment(property, segments);
                var closing = FindClosingBracket(trimmed, i + 1);
                if (closing < 0)
                    throw new InvalidOperationException($"path '{path}' contains '[' without closing ']'.");

                var indexText = trimmed.Substring(i + 1, closing - i - 1).Trim();
                if (indexText == "*")
                {
                    segments.Add(new ModelPathSegment { Kind = ModelPathSegmentKind.Wildcard });
                    i = closing;
                    continue;
                }
                if (indexText == "**")
                {
                    segments.Add(new ModelPathSegment { Kind = ModelPathSegmentKind.RecursiveWildcard });
                    i = closing;
                    continue;
                }

                if (TryParseQuotedPathProperty(indexText, out var quotedProperty))
                {
                    segments.Add(new ModelPathSegment { Kind = ModelPathSegmentKind.Property, Property = quotedProperty });
                    i = closing;
                    continue;
                }

                if (int.TryParse(indexText, out var index) && index >= 0)
                {
                    segments.Add(new ModelPathSegment { Kind = ModelPathSegmentKind.Index, Index = index });
                    i = closing;
                    continue;
                }

                if (indexText.Length == 0)
                    throw new InvalidOperationException($"path '{path}' contains empty bracket segment.");

                segments.Add(new ModelPathSegment { Kind = ModelPathSegmentKind.Property, Property = indexText });
                i = closing;
                continue;
            }

            property.Append(c);
        }

        FlushPropertySegment(property, segments);
        return segments;
    }

    private static void FlushPropertySegment(StringBuilder property, List<ModelPathSegment> segments)
    {
        if (property.Length == 0)
            return;

        var value = property.ToString().Trim();
        property.Clear();
        if (value.Length == 0)
            return;

        if (value == "*")
        {
            segments.Add(new ModelPathSegment
            {
                Kind = ModelPathSegmentKind.Wildcard
            });
            return;
        }
        if (value == "**")
        {
            segments.Add(new ModelPathSegment
            {
                Kind = ModelPathSegmentKind.RecursiveWildcard
            });
            return;
        }

        segments.Add(new ModelPathSegment
        {
            Kind = ModelPathSegmentKind.Property,
            Property = value
        });
    }

    private static int FindClosingBracket(string value, int startIndex)
    {
        char? quote = null;
        var escaped = false;
        for (var i = startIndex; i < value.Length; i++)
        {
            var c = value[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (quote.HasValue)
            {
                if (c == quote.Value)
                    quote = null;
                continue;
            }

            if (c == '\'' || c == '"')
            {
                quote = c;
                continue;
            }

            if (c == ']')
                return i;
        }

        return -1;
    }

    private static bool TryParseQuotedPathProperty(string value, out string property)
    {
        property = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length < 2)
            return false;

        var quote = trimmed[0];
        if ((quote != '\'' && quote != '"') || trimmed[^1] != quote)
            return false;

        var builder = new StringBuilder();
        var escaped = false;
        for (var i = 1; i < trimmed.Length - 1; i++)
        {
            var c = trimmed[i];
            if (escaped)
            {
                builder.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            builder.Append(c);
        }

        if (escaped)
            throw new InvalidOperationException($"path segment '{value}' ends with dangling escape.");

        property = builder.ToString();
        return true;
    }

    private static JsonNode? CloneNode(JsonNode? node)
    {
        return node?.DeepClone();
    }

    private static string SerializeModelTransformOutput(JsonNode? root, bool pretty)
    {
        return root?.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = pretty
        }) ?? "null";
    }

    private static void WriteModelTransformReport(string reportPath, ModelTransformReport report)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(reportPath, json, Encoding.UTF8);
    }

    private enum ModelPathSegmentKind
    {
        Property,
        Index,
        Wildcard,
        RecursiveWildcard
    }

    private sealed class ModelPathSegment
    {
        public ModelPathSegmentKind Kind { get; set; }
        public string? Property { get; set; }
        public int? Index { get; set; }
    }

    private sealed class ModelTransformOperationReport
    {
        public int Index { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string? Path { get; set; }
        public int TargetsApplied { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    private sealed class ModelTransformReport
    {
        public string Input { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public bool Strict { get; set; }
        public bool Pretty { get; set; }
        public bool ValidateJson { get; set; }
        public int OperationsTotal { get; set; }
        public int OperationsSucceeded { get; set; }
        public int OperationsFailed { get; set; }
        public bool Changed { get; set; }
        public string Utc { get; set; } = string.Empty;
        public List<ModelTransformOperationReport> Operations { get; set; } = new();
    }
}
