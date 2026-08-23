using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PowerForge;

/// <summary>
/// Verifies generated member access against the requested target framework reference assemblies.
/// </summary>
internal static class PowerShellGeneratedMemberPolicy
{
    private static readonly ConcurrentDictionary<string, Lazy<HashSet<string>>> TargetMembers = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsSupported(MemberInfo member, string targetFramework)
    {
        var key = CreateReflectionKey(member);
        return key is not null && GetTargetMembers(targetFramework).Contains(key);
    }

    private static HashSet<string> GetTargetMembers(string targetFramework)
        => TargetMembers.GetOrAdd(
            targetFramework,
            static framework => new Lazy<HashSet<string>>(
                () => ReadTargetMembers(framework),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static HashSet<string> ReadTargetMembers(string targetFramework)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in PowerShellGeneratedTypePolicy.GetReferenceAssemblyPaths(targetFramework))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata)
                    continue;
                var reader = pe.GetMetadataReader();
                var provider = new SignatureNameProvider(reader);
                foreach (var typeHandle in reader.TypeDefinitions)
                {
                    var type = reader.GetTypeDefinition(typeHandle);
                    var typeName = PowerShellGeneratedTypePolicy.GetTypeDefinitionName(reader, typeHandle);
                    foreach (var methodHandle in type.GetMethods())
                    {
                        var method = reader.GetMethodDefinition(methodHandle);
                        var signature = method.DecodeSignature(provider, genericContext: null);
                        members.Add(CreateMethodKey(
                            typeName,
                            reader.GetString(method.Name),
                            (method.Attributes & MethodAttributes.Static) != 0,
                            signature.ParameterTypes));
                    }
                    foreach (var fieldHandle in type.GetFields())
                    {
                        var field = reader.GetFieldDefinition(fieldHandle);
                        members.Add(CreateSimpleKey(
                            typeName,
                            "F",
                            reader.GetString(field.Name),
                            (field.Attributes & FieldAttributes.Static) != 0));
                    }
                    foreach (var propertyHandle in type.GetProperties())
                    {
                        var property = reader.GetPropertyDefinition(propertyHandle);
                        var accessors = property.GetAccessors();
                        var accessorHandle = !accessors.Getter.IsNil ? accessors.Getter : accessors.Setter;
                        if (accessorHandle.IsNil)
                            continue;
                        var accessor = reader.GetMethodDefinition(accessorHandle);
                        members.Add(CreateSimpleKey(
                            typeName,
                            "P",
                            reader.GetString(property.Name),
                            (accessor.Attributes & MethodAttributes.Static) != 0));
                    }
                }
            }
            catch (BadImageFormatException)
            {
                // Native reference assets cannot contribute CLR members.
            }
        }
        return members;
    }

    private static string? CreateReflectionKey(MemberInfo member)
    {
        var declaringType = member.DeclaringType?.FullName;
        if (string.IsNullOrWhiteSpace(declaringType))
            return null;
        return member switch
        {
            MethodBase method => CreateMethodKey(
                declaringType!,
                method is ConstructorInfo ? ".ctor" : method.Name,
                method.IsStatic,
                method.GetParameters().Select(static parameter => GetReflectionTypeName(parameter.ParameterType))),
            PropertyInfo property => CreateSimpleKey(
                declaringType!,
                "P",
                property.Name,
                (property.GetMethod ?? property.SetMethod)?.IsStatic == true),
            FieldInfo field => CreateSimpleKey(declaringType!, "F", field.Name, field.IsStatic),
            _ => null
        };
    }

    private static string CreateMethodKey(string type, string name, bool isStatic, IEnumerable<string> parameters)
        => type + "|M|" + name + "|" + (isStatic ? "S" : "I") + "|" + string.Join(",", parameters);

    private static string CreateSimpleKey(string type, string kind, string name, bool isStatic)
        => type + "|" + kind + "|" + name + "|" + (isStatic ? "S" : "I");

    private static string GetReflectionTypeName(Type type)
    {
        if (type.IsByRef)
            return GetReflectionTypeName(type.GetElementType()!) + "&";
        return type.FullName ?? type.Name;
    }

    private sealed class SignatureNameProvider : ISignatureTypeProvider<string, object?>
    {
        private readonly MetadataReader _reader;

        internal SignatureNameProvider(MetadataReader reader) => _reader = reader;

        public string GetArrayType(string elementType, ArrayShape shape)
            => elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => genericType + "[" + string.Join(",", typeArguments) + "]";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => typeof(bool).FullName!,
            PrimitiveTypeCode.Byte => typeof(byte).FullName!,
            PrimitiveTypeCode.Char => typeof(char).FullName!,
            PrimitiveTypeCode.Double => typeof(double).FullName!,
            PrimitiveTypeCode.Int16 => typeof(short).FullName!,
            PrimitiveTypeCode.Int32 => typeof(int).FullName!,
            PrimitiveTypeCode.Int64 => typeof(long).FullName!,
            PrimitiveTypeCode.IntPtr => typeof(IntPtr).FullName!,
            PrimitiveTypeCode.Object => typeof(object).FullName!,
            PrimitiveTypeCode.SByte => typeof(sbyte).FullName!,
            PrimitiveTypeCode.Single => typeof(float).FullName!,
            PrimitiveTypeCode.String => typeof(string).FullName!,
            PrimitiveTypeCode.UInt16 => typeof(ushort).FullName!,
            PrimitiveTypeCode.UInt32 => typeof(uint).FullName!,
            PrimitiveTypeCode.UInt64 => typeof(ulong).FullName!,
            PrimitiveTypeCode.UIntPtr => typeof(UIntPtr).FullName!,
            PrimitiveTypeCode.Void => typeof(void).FullName!,
            _ => typeCode.ToString()
        };
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => PowerShellGeneratedTypePolicy.GetTypeDefinitionName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => PowerShellGeneratedTypePolicy.GetTypeReferenceName(reader, handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
