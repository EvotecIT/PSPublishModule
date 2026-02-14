using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private delegate JsonNode? ModelPathOperation(
        JsonNode? root,
        string? path,
        int? index,
        JsonNode? value,
        string operationType,
        bool strict);

    private static ModelTransformApplyResult ApplyPathOperation(
        JsonNode? root,
        string? path,
        ModelPathOperation operation,
        bool strict,
        JsonNode? value,
        int? index,
        string operationType,
        ModelTransformTargetConstraints? targetConstraints,
        ModelTransformOperationCondition? operationCondition)
    {
        var targets = ResolveOperationTargets(root, path, operationType);
        var applied = 0;
        foreach (var target in targets)
        {
            if (!EvaluateOperationCondition(root, target.Path, operationCondition))
                continue;
            root = operation(root, target.Path, index, value, operationType, strict);
            applied++;
        }

        ValidateOperationTargetConstraints(operationType, path, applied, targetConstraints);

        return new ModelTransformApplyResult
        {
            Root = root,
            TargetsApplied = applied
        };
    }

    private static ModelTransformApplyResult ApplyTransferOperation(
        JsonNode? root,
        string? destinationPath,
        string sourcePath,
        bool strict,
        bool removeSource,
        ModelTransformTargetConstraints? targetConstraints,
        ModelTransformOperationCondition? operationCondition,
        ModelTransformOperationCondition? sourceCondition)
    {
        var sourceTargets = ResolveOperationTargets(root, sourcePath, removeSource ? "move" : "copy");
        var filteredSourceTargets = sourceTargets
            .Where(source => EvaluateOperationCondition(root, source.Path, sourceCondition))
            .ToList();
        var destinationTargets = ResolveOperationTargets(root, destinationPath, removeSource ? "move" : "copy");
        var filteredDestinationTargets = destinationTargets
            .Where(destination => EvaluateOperationCondition(root, destination.Path, operationCondition))
            .ToList();

        var sourceWildcard = ContainsWildcardPath(sourcePath);
        var destinationWildcard = ContainsWildcardPath(destinationPath);

        if (sourceWildcard && !destinationWildcard && filteredSourceTargets.Count != 1)
            throw new InvalidOperationException(
                $"{(removeSource ? "move" : "copy")} source path '{sourcePath}' matched {filteredSourceTargets.Count} targets after source condition filtering, but destination '{destinationPath}' is a single target.");

        if (sourceWildcard && destinationWildcard && filteredSourceTargets.Count != filteredDestinationTargets.Count)
            throw new InvalidOperationException(
                $"{(removeSource ? "move" : "copy")} wildcard source '{sourcePath}' matched {filteredSourceTargets.Count} targets but destination '{destinationPath}' matched {filteredDestinationTargets.Count} after condition filtering.");

        if (sourceWildcard && filteredSourceTargets.Count > 0)
        {
            if (destinationWildcard)
            {
                for (var i = 0; i < filteredSourceTargets.Count; i++)
                {
                    var sourceValue = GetNodeAtPath(root, filteredSourceTargets[i].Path);
                    root = SetNodeAtPath(root, filteredDestinationTargets[i].Path, sourceValue, strict);
                }
            }
            else if (filteredDestinationTargets.Count > 0)
            {
                var sourceValue = GetNodeAtPath(root, filteredSourceTargets[0].Path);
                root = SetNodeAtPath(root, destinationPath, sourceValue, strict);
            }
        }
        else
        {
            if (filteredSourceTargets.Count > 0)
            {
                var sourceValue = GetNodeAtPath(root, sourcePath);
                foreach (var destinationTarget in filteredDestinationTargets)
                    root = SetNodeAtPath(root, destinationTarget.Path, sourceValue, strict);
            }
        }

        var appliedTargets = filteredDestinationTargets.Count;
        if (removeSource && appliedTargets > 0)
        {
            var sourcesToRemove = sourceWildcard
                ? filteredSourceTargets
                : new List<ModelTransformResolvedPath> { filteredSourceTargets[0] };
            foreach (var sourceTarget in OrderTargetsForRemoval(sourcesToRemove))
                root = RemoveNodeAtPath(root, sourceTarget.Path);
        }

        ValidateOperationTargetConstraints(removeSource ? "move" : "copy", destinationPath, appliedTargets, targetConstraints);

        return new ModelTransformApplyResult
        {
            Root = root,
            TargetsApplied = appliedTargets
        };
    }

    private static List<ModelTransformResolvedPath> ResolveOperationTargets(JsonNode? root, string? path, string operationType)
    {
        var segments = ParsePath(path);
        var hasWildcard = segments.Any(static segment =>
            segment.Kind == ModelPathSegmentKind.Wildcard ||
            segment.Kind == ModelPathSegmentKind.RecursiveWildcard);
        if (!hasWildcard)
        {
            return new List<ModelTransformResolvedPath>
            {
                new()
                {
                    Path = path ?? "$",
                    Segments = segments
                }
            };
        }

        var resolved = ResolveWildcardPaths(root, segments);
        if (resolved.Count == 0)
            throw new InvalidOperationException($"{operationType} path '{path}' matched no targets.");

        return operationType.Equals("remove", StringComparison.OrdinalIgnoreCase)
            ? OrderTargetsForRemoval(resolved)
            : resolved;
    }

    private static bool ContainsWildcardPath(string? path)
    {
        return ParsePath(path).Any(static segment =>
            segment.Kind == ModelPathSegmentKind.Wildcard ||
            segment.Kind == ModelPathSegmentKind.RecursiveWildcard);
    }

    private static List<ModelTransformResolvedPath> ResolveWildcardPaths(JsonNode? root, List<ModelPathSegment> segments)
    {
        if (segments.Count == 0)
        {
            return new List<ModelTransformResolvedPath>
            {
                new()
                {
                    Path = "$",
                    Segments = new List<ModelPathSegment>()
                }
            };
        }

        if (root is null)
            return new List<ModelTransformResolvedPath>();

        var resolved = new List<ModelTransformResolvedPath>();
        ResolveWildcardPathsRecursive(root, segments, 0, new List<ModelPathSegment>(), resolved);
        return DeduplicateResolvedPaths(resolved);
    }

    private static void ResolveWildcardPathsRecursive(
        JsonNode? current,
        List<ModelPathSegment> segments,
        int index,
        List<ModelPathSegment> concreteSegments,
        List<ModelTransformResolvedPath> resolved)
    {
        if (index >= segments.Count)
        {
            resolved.Add(new ModelTransformResolvedPath
            {
                Path = BuildPath(concreteSegments),
                Segments = new List<ModelPathSegment>(concreteSegments)
            });
            return;
        }

        if (current is null)
            return;

        var segment = segments[index];
        if (segment.Kind == ModelPathSegmentKind.RecursiveWildcard)
        {
            ResolveWildcardPathsRecursive(current, segments, index + 1, concreteSegments, resolved);
            EnumerateChildNodes(current, static (childSegment, childNode, state) =>
            {
                state.ConcreteSegments.Add(childSegment);
                ResolveWildcardPathsRecursive(childNode, state.Segments, state.Index, state.ConcreteSegments, state.Resolved);
                state.ConcreteSegments.RemoveAt(state.ConcreteSegments.Count - 1);
            }, new RecursiveWildcardState
            {
                Segments = segments,
                Index = index,
                ConcreteSegments = concreteSegments,
                Resolved = resolved
            });
            return;
        }

        if (segment.Kind != ModelPathSegmentKind.Wildcard)
        {
            if (!CanAddressSegment(current, segment))
                return;

            var next = TryGetChildNode(current, segment);
            if (next is null && index < segments.Count - 1)
                return;

            concreteSegments.Add(segment);
            ResolveWildcardPathsRecursive(next, segments, index + 1, concreteSegments, resolved);
            concreteSegments.RemoveAt(concreteSegments.Count - 1);
            return;
        }

        EnumerateChildNodes(current, static (childSegment, childNode, state) =>
        {
            state.ConcreteSegments.Add(childSegment);
            ResolveWildcardPathsRecursive(childNode, state.Segments, state.Index + 1, state.ConcreteSegments, state.Resolved);
            state.ConcreteSegments.RemoveAt(state.ConcreteSegments.Count - 1);
        }, new WildcardState
        {
            Segments = segments,
            Index = index,
            ConcreteSegments = concreteSegments,
            Resolved = resolved
        });
    }

    private static bool CanAddressSegment(JsonNode current, ModelPathSegment segment)
    {
        return segment.Kind switch
        {
            ModelPathSegmentKind.Property => current is JsonObject,
            ModelPathSegmentKind.Index => current is JsonArray && segment.Index.GetValueOrDefault() >= 0,
            _ => false
        };
    }

    private static void EnumerateChildNodes<TState>(
        JsonNode current,
        Action<ModelPathSegment, JsonNode?, TState> visitor,
        TState state)
    {
        switch (current)
        {
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    visitor(
                        new ModelPathSegment
                        {
                            Kind = ModelPathSegmentKind.Index,
                            Index = i
                        },
                        array[i],
                        state);
                }

                break;
            case JsonObject obj:
                foreach (var key in obj.Select(static pair => pair.Key).OrderBy(static key => key, StringComparer.Ordinal))
                {
                    visitor(
                        new ModelPathSegment
                        {
                            Kind = ModelPathSegmentKind.Property,
                            Property = key
                        },
                        obj[key],
                        state);
                }

                break;
        }
    }

    private static List<ModelTransformResolvedPath> DeduplicateResolvedPaths(List<ModelTransformResolvedPath> resolved)
    {
        if (resolved.Count <= 1)
            return resolved;

        var output = new List<ModelTransformResolvedPath>(resolved.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in resolved)
        {
            if (seen.Add(item.Path))
                output.Add(item);
        }

        return output;
    }

    private static List<ModelTransformResolvedPath> OrderTargetsForRemoval(List<ModelTransformResolvedPath> targets)
    {
        return targets
            .OrderByDescending(static target => target, ModelTransformResolvedPathRemovalComparer.Instance)
            .ToList();
    }

    private static string BuildPath(List<ModelPathSegment> segments)
    {
        if (segments.Count == 0)
            return "$";

        var builder = new StringBuilder();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.Kind == ModelPathSegmentKind.Property)
            {
                var property = segment.Property ?? string.Empty;
                if (IsSimplePropertyPathSegment(property))
                {
                    if (builder.Length > 0)
                        builder.Append('.');
                    builder.Append(property);
                }
                else
                {
                    builder.Append("['");
                    builder.Append(EscapePathPropertySegment(property));
                    builder.Append("']");
                }
            }
            else if (segment.Kind == ModelPathSegmentKind.Index)
            {
                builder.Append('[');
                builder.Append(segment.Index.GetValueOrDefault());
                builder.Append(']');
            }
        }

        return builder.ToString();
    }

    private static bool IsSimplePropertyPathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsLetterOrDigit(c) || c == '_')
                continue;
            return false;
        }

        return true;
    }

    private static string EscapePathPropertySegment(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }

    private sealed class ModelTransformResolvedPath
    {
        public string Path { get; set; } = string.Empty;
        public List<ModelPathSegment> Segments { get; set; } = new();
    }

    private sealed class WildcardState
    {
        public List<ModelPathSegment> Segments { get; set; } = new();
        public int Index { get; set; }
        public List<ModelPathSegment> ConcreteSegments { get; set; } = new();
        public List<ModelTransformResolvedPath> Resolved { get; set; } = new();
    }

    private sealed class RecursiveWildcardState
    {
        public List<ModelPathSegment> Segments { get; set; } = new();
        public int Index { get; set; }
        public List<ModelPathSegment> ConcreteSegments { get; set; } = new();
        public List<ModelTransformResolvedPath> Resolved { get; set; } = new();
    }

    private sealed class ModelTransformResolvedPathRemovalComparer : IComparer<ModelTransformResolvedPath>
    {
        public static ModelTransformResolvedPathRemovalComparer Instance { get; } = new();

        public int Compare(ModelTransformResolvedPath? x, ModelTransformResolvedPath? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var min = Math.Min(x.Segments.Count, y.Segments.Count);
            for (var i = 0; i < min; i++)
            {
                var left = x.Segments[i];
                var right = y.Segments[i];

                if (left.Kind == ModelPathSegmentKind.Index && right.Kind == ModelPathSegmentKind.Index)
                {
                    var cmp = left.Index.GetValueOrDefault().CompareTo(right.Index.GetValueOrDefault());
                    if (cmp != 0)
                        return cmp;
                    continue;
                }

                if (left.Kind != right.Kind)
                    return left.Kind.CompareTo(right.Kind);

                if (left.Kind == ModelPathSegmentKind.Property)
                {
                    var cmp = string.CompareOrdinal(left.Property, right.Property);
                    if (cmp != 0)
                        return cmp;
                }
            }

            if (x.Segments.Count != y.Segments.Count)
                return x.Segments.Count.CompareTo(y.Segments.Count);

            return string.CompareOrdinal(x.Path, y.Path);
        }
    }

    private sealed class ModelTransformApplyResult
    {
        public JsonNode? Root { get; set; }
        public int TargetsApplied { get; set; }
    }
}
