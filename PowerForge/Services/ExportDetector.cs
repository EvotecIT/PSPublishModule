using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Language;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PowerForge;

/// <summary>
/// Detects public functions from scripts and cmdlets/aliases from binaries for manifest export lists.
/// </summary>
public static class ExportDetector
{
    /// <summary>Detects function names defined in the provided PowerShell script files.</summary>
    public static IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in scriptFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;
            try
            {
                Token[] tokens; ParseError[] errors;
                var ast = Parser.ParseFile(file, out tokens, out errors);
                if (errors != null && errors.Length > 0) continue;
                var funcs = ast.FindAll(a => a is FunctionDefinitionAst, true).Cast<FunctionDefinitionAst>();
                foreach (var f in funcs)
                {
                    // Use the declared function name; skip private helper names starting with '_' by convention
                    var name = f.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.Add(name);
                }
            }
            catch { /* ignore */ }
        }
        return result.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Detects cmdlet names from the provided assemblies by scanning for CmdletAttribute.</summary>
    public static IReadOnlyList<string> DetectBinaryCmdlets(IEnumerable<string> assemblyPaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in assemblyPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            foreach (var cmdlet in ScanAssemblyForCmdlets(path)) set.Add(cmdlet);
        }
        return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Detects aliases from the provided assemblies by scanning for AliasAttribute on cmdlet classes.</summary>
    public static IReadOnlyList<string> DetectBinaryAliases(IEnumerable<string> assemblyPaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in assemblyPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            foreach (var alias in ScanAssemblyForAliases(path)) set.Add(alias);
        }
        return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ScanAssemblyForCmdlets(string assemblyPath)
    {
        var list = new List<string>();
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata) return list;

            var reader = pe.GetMetadataReader();
            foreach (var tHandle in reader.TypeDefinitions)
            {
                var t = reader.GetTypeDefinition(tHandle);
                foreach (var caHandle in t.GetCustomAttributes())
                {
                    var ca = reader.GetCustomAttribute(caHandle);
                    var fullName = GetCustomAttributeTypeFullName(reader, ca);
                    if (!string.Equals(fullName, "System.Management.Automation.CmdletAttribute", StringComparison.Ordinal))
                        continue;

                    if (TryReadCmdletAttribute(reader, ca, out var verb, out var noun))
                        list.Add(verb + "-" + noun);
                }
            }
        }
        catch { /* best effort */ }
        return list;
    }

    private static IEnumerable<string> ScanAssemblyForAliases(string assemblyPath)
    {
        var list = new List<string>();
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata) return list;

            var reader = pe.GetMetadataReader();
            foreach (var tHandle in reader.TypeDefinitions)
            {
                var t = reader.GetTypeDefinition(tHandle);
                foreach (var caHandle in t.GetCustomAttributes())
                {
                    var ca = reader.GetCustomAttribute(caHandle);
                    var fullName = GetCustomAttributeTypeFullName(reader, ca);
                    if (!string.Equals(fullName, "System.Management.Automation.AliasAttribute", StringComparison.Ordinal))
                        continue;

                    foreach (var alias in ReadAliasAttribute(reader, ca))
                        list.Add(alias);
                }
            }
        }
        catch { /* best effort */ }
        return list;
    }

    private static string GetCustomAttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        try
        {
            var ctor = attribute.Constructor;
            return ctor.Kind switch
            {
                HandleKind.MemberReference => GetTypeFullName(reader, reader.GetMemberReference((MemberReferenceHandle)ctor).Parent),
                HandleKind.MethodDefinition => GetTypeFullName(reader, reader.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType()),
                _ => string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetTypeFullName(MetadataReader reader, EntityHandle typeHandle)
    {
        try
        {
            return typeHandle.Kind switch
            {
                HandleKind.TypeReference => CombineNamespaceAndName(reader, reader.GetTypeReference((TypeReferenceHandle)typeHandle)),
                HandleKind.TypeDefinition => CombineNamespaceAndName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle)),
                _ => string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CombineNamespaceAndName(MetadataReader reader, TypeReference type)
        => CombineNamespaceAndName(type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace),
            type.Name.IsNil ? string.Empty : reader.GetString(type.Name));

    private static string CombineNamespaceAndName(MetadataReader reader, TypeDefinition type)
        => CombineNamespaceAndName(type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace),
            type.Name.IsNil ? string.Empty : reader.GetString(type.Name));

    private static string CombineNamespaceAndName(string? ns, string? name)
    {
        ns ??= string.Empty;
        name ??= string.Empty;
        if (ns.Length == 0) return name;
        if (name.Length == 0) return ns;
        return ns + "." + name;
    }

    private static bool TryReadCmdletAttribute(MetadataReader reader, CustomAttribute attribute, out string verb, out string noun)
    {
        verb = string.Empty;
        noun = string.Empty;
        try
        {
            var br = reader.GetBlobReader(attribute.Value);
            if (br.ReadUInt16() != 1) return false;

            var v = br.ReadSerializedString();
            var n = br.ReadSerializedString();
            if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(n)) return false;

            verb = v!;
            noun = n!;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private enum AliasConstructorKind
    {
        Unknown,
        String,
        StringArray
    }

    private static IEnumerable<string> ReadAliasAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        var list = new List<string>();

        try
        {
            var kind = GetAliasConstructorKind(reader, attribute.Constructor);
            if (kind == AliasConstructorKind.String)
            {
                var br = reader.GetBlobReader(attribute.Value);
                if (br.ReadUInt16() != 1) return list;
                var s = br.ReadSerializedString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                return list;
            }

            // Default: string[] (params)
            {
                var br = reader.GetBlobReader(attribute.Value);
                if (br.ReadUInt16() != 1) return list;
                var count = br.ReadInt32();
                if (count <= 0) return list;
                if (count > 10_000) return list; // sanity guard

                for (var i = 0; i < count; i++)
                {
                    var s = br.ReadSerializedString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                }

                return list;
            }
        }
        catch
        {
            return list;
        }
    }

    private static AliasConstructorKind GetAliasConstructorKind(MetadataReader reader, EntityHandle ctorHandle)
    {
        try
        {
            BlobReader sig = ctorHandle.Kind switch
            {
                HandleKind.MemberReference => reader.GetBlobReader(reader.GetMemberReference((MemberReferenceHandle)ctorHandle).Signature),
                HandleKind.MethodDefinition => reader.GetBlobReader(reader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle).Signature),
                _ => default
            };

            if (sig.Length == 0) return AliasConstructorKind.Unknown;

            _ = sig.ReadByte(); // calling convention
            var paramCount = sig.ReadCompressedInteger();

            // return type (constructors are void)
            if (sig.Offset < sig.Length) _ = sig.ReadByte();

            if (paramCount <= 0) return AliasConstructorKind.Unknown;

            return ReadAliasParamKind(sig);
        }
        catch
        {
            return AliasConstructorKind.Unknown;
        }
    }

    private static AliasConstructorKind ReadAliasParamKind(BlobReader signature)
    {
        try
        {
            if (signature.Offset >= signature.Length) return AliasConstructorKind.Unknown;

            // ELEMENT_TYPE_STRING (0x0e) or ELEMENT_TYPE_SZARRAY (0x1d) of string
            var typeCode = signature.ReadByte();
            if (typeCode == 0x0e) return AliasConstructorKind.String;
            if (typeCode == 0x1d)
            {
                if (signature.Offset >= signature.Length) return AliasConstructorKind.Unknown;
                var element = signature.ReadByte();
                return element == 0x0e ? AliasConstructorKind.StringArray : AliasConstructorKind.Unknown;
            }

            return AliasConstructorKind.Unknown;
        }
        catch
        {
            return AliasConstructorKind.Unknown;
        }
    }
}
