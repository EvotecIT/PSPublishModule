namespace PowerForge.Compilation.Build
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Reflection.Metadata.Ecma335;
    using System.Reflection.PortableExecutable;
    using System.Text;

    /// <summary>Projects authored command names into reserved CLR metadata storage after C# compilation.</summary>
    internal static partial class PowerShellCommandMetadataNames
    {
        internal sealed class Identity
        {
            internal Identity(string typeName, string commandName, string storageField)
            {
                TypeName = typeName;
                CommandName = commandName;
                StorageField = storageField;
            }
            internal string TypeName { get; }
            internal string CommandName { get; }
            internal string StorageField { get; }
        }

        internal static bool CanRepresent(string commandName)
            => !string.IsNullOrWhiteSpace(commandName) && commandName.IndexOf('-') > 0 &&
               !commandName.Any(character => char.IsControl(character) || "\\+[],&*".IndexOf(character) >= 0);

        internal static byte[] Apply(byte[] image, IReadOnlyList<Identity> identities)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            if (identities is null) throw new ArgumentNullException(nameof(identities));
            if (identities.Count == 0) return image;
            if (identities.Select(identity => identity.TypeName).Distinct(StringComparer.Ordinal).Count() != identities.Count ||
                identities.Select(identity => identity.CommandName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != identities.Count)
                throw new InvalidDataException("The command identity plan contains duplicate type or command names.");

            using (var stream = new MemoryStream(image, writable: false))
            using (var pe = new PEReader(stream))
            {
                if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null)
                    throw new InvalidDataException("Command identities require a managed assembly.");
                var reader = pe.GetMetadataReader();
                if (!reader.IsAssembly || !pe.PEHeaders.TryGetDirectoryOffset(pe.PEHeaders.CorHeader.MetadataDirectory, out var metadataOffset))
                    throw new InvalidDataException("The command assembly has no complete CLR metadata directory.");
                var stringIndexSize = reader.GetHeapSize(HeapIndex.String) > ushort.MaxValue ? 4 : 2;
                var stringHeapOffset = checked(metadataOffset + reader.GetHeapMetadataOffset(HeapIndex.String));
                var stringReferences = ReadStringReferences(reader).ToArray();
                var patches = new List<NamePatch>();
                foreach (var identity in identities)
                {
                    if (!CanRepresent(identity.CommandName) || string.IsNullOrWhiteSpace(identity.TypeName) ||
                        string.IsNullOrWhiteSpace(identity.StorageField))
                        throw new InvalidDataException("The command identity plan contains an unsupported CLR display name or storage field.");
                    var original = FindType(reader, identity.TypeName);
                    var final = FindType(reader, identity.CommandName);
                    if (!original.IsNil && !final.IsNil)
                        throw new InvalidDataException("Command identity '" + identity.CommandName + "' collides with an existing CLR type.");
                    if (original.IsNil && final.IsNil)
                        throw new InvalidDataException("Generated command type '" + identity.TypeName + "' was not found.");
                    var typeHandle = original.IsNil ? final : original;
                    var type = reader.GetTypeDefinition(typeHandle);
                    ValidateCmdletBase(reader, type);
                    var expectedField = original.IsNil ? identity.CommandName : identity.StorageField;
                    var fields = type.GetFields().Where(handle => reader.GetString(reader.GetFieldDefinition(handle).Name) == expectedField).ToArray();
                    if (fields.Length != 1) throw new InvalidDataException("The command identity storage field is missing or ambiguous.");
                    var field = reader.GetFieldDefinition(fields[0]);
                    ValidateStorageField(reader, field, identity.CommandName);
                    if (original.IsNil) continue;

                    var storage = MetadataTokens.GetHeapOffset(field.Name);
                    var encoded = new UTF8Encoding(false, true).GetBytes(identity.CommandName);
                    var reservedLength = Encoding.UTF8.GetByteCount(reader.GetString(field.Name));
                    if (encoded.Length >= reservedLength)
                        throw new InvalidDataException("The command identity storage does not have the required reserved capacity.");
                    var storageEnd = checked(storage + encoded.Length + 1);
                    var fieldToken = MetadataTokens.GetToken(fields[0]);
                    if (stringReferences.Any(reference => reference.OwnerToken != fieldToken && reference.Offset < storageEnd &&
                        reference.Offset + Encoding.UTF8.GetByteCount(reader.GetString(MetadataTokens.StringHandle(reference.Offset))) + 1 > storage))
                        throw new InvalidDataException("The reserved command identity storage overlaps another metadata name.");
                    if (patches.Any(patch => storage < patch.Storage + patch.Name.Length + 1 && patch.Storage < storageEnd))
                        throw new InvalidDataException("The command identity storage ranges overlap.");
                    var typeRow = checked(metadataOffset + reader.GetTableMetadataOffset(TableIndex.TypeDef) +
                        (MetadataTokens.GetRowNumber(typeHandle) - 1) * reader.GetTableRowSize(TableIndex.TypeDef));
                    patches.Add(new NamePatch(typeRow + 4, storage, encoded));
                }
                if (patches.Count == 0) return image;
                if ((pe.PEHeaders.CorHeader.Flags & CorFlags.StrongNameSigned) != 0 ||
                    pe.PEHeaders.CorHeader.StrongNameSignatureDirectory.Size != 0 ||
                    pe.PEHeaders.PEHeader?.CertificateTableDirectory.Size > 0 ||
                    pe.PEHeaders.CorHeader.ManagedNativeHeaderDirectory.Size != 0)
                    throw new InvalidDataException("Command metadata names must be finalized before signing or native-image generation.");

                var result = (byte[])image.Clone();
                foreach (var patch in patches)
                {
                    var offset = checked(stringHeapOffset + patch.Storage);
                    Array.Copy(patch.Name, 0, result, offset, patch.Name.Length);
                    result[offset + patch.Name.Length] = 0;
                    WriteIndex(result, patch.TypeNameOffset, patch.Storage, stringIndexSize);
                    WriteIndex(result, patch.TypeNameOffset + stringIndexSize, 0, stringIndexSize);
                }
                return result;
            }
        }

        private static TypeDefinitionHandle FindType(MetadataReader reader, string fullName)
        {
            var matches = reader.TypeDefinitions.Where(handle =>
            {
                var definition = reader.GetTypeDefinition(handle);
                var name = reader.GetString(definition.Name);
                var ns = reader.GetString(definition.Namespace);
                return (ns.Length == 0 ? name : ns + "." + name) == fullName && definition.GetDeclaringType().IsNil;
            }).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("The command identity plan matches more than one CLR type.");
            return matches.Length == 0 ? default(TypeDefinitionHandle) : matches[0];
        }

        private static void ValidateCmdletBase(MetadataReader reader, TypeDefinition type)
        {
            if (type.BaseType.Kind != HandleKind.TypeReference)
                throw new InvalidDataException("Command identity storage must belong to a generated PSCmdlet.");
            var reference = reader.GetTypeReference((TypeReferenceHandle)type.BaseType);
            if (reader.GetString(reference.Name) != "PSCmdlet" || reader.GetString(reference.Namespace) != "System.Management.Automation" ||
                reference.ResolutionScope.Kind != HandleKind.AssemblyReference ||
                reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name) != "System.Management.Automation")
                throw new InvalidDataException("Command identity storage must belong to a generated PSCmdlet.");
        }

        private static void ValidateStorageField(MetadataReader reader, FieldDefinition field, string commandName)
        {
            if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Private ||
                (field.Attributes & (FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault)) !=
                    (FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault) || field.GetDefaultValue().IsNil)
                throw new InvalidDataException("The command identity storage field is not a private constant.");
            var constant = reader.GetConstant(field.GetDefaultValue());
            if (constant.TypeCode != ConstantTypeCode.String ||
                Encoding.Unicode.GetString(reader.GetBlobBytes(constant.Value)) != commandName)
                throw new InvalidDataException("The command identity storage constant does not match the authored name.");
        }

        private static void WriteIndex(byte[] bytes, int offset, int value, int size)
        {
            if (value < 0 || size == 2 && value > ushort.MaxValue)
                throw new InvalidDataException("The command identity cannot be represented by the metadata string index.");
            for (var index = 0; index < size; index++) bytes[offset + index] = (byte)(value >> (8 * index));
        }

        private sealed class NamePatch
        {
            internal NamePatch(int typeNameOffset, int storage, byte[] name)
            {
                TypeNameOffset = typeNameOffset;
                Storage = storage;
                Name = name;
            }
            internal int TypeNameOffset { get; }
            internal int Storage { get; }
            internal byte[] Name { get; }
        }
    }
}
