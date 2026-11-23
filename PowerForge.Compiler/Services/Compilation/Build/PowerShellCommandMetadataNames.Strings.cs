namespace PowerForge.Compilation.Build
{
    using System.Collections.Generic;
    using System.Reflection.Metadata;
    using System.Reflection.Metadata.Ecma335;

    internal static partial class PowerShellCommandMetadataNames
    {
        private static IEnumerable<StringReference> ReadStringReferences(MetadataReader reader)
        {
            yield return Reference(reader.GetModuleDefinition().Name);
            if (reader.IsAssembly)
            {
                var assembly = reader.GetAssemblyDefinition();
                yield return Reference(assembly.Name);
                yield return Reference(assembly.Culture);
            }
            foreach (var handle in reader.TypeDefinitions)
            {
                var definition = reader.GetTypeDefinition(handle);
                yield return Reference(definition.Name);
                yield return Reference(definition.Namespace);
            }
            foreach (var handle in reader.TypeReferences)
            {
                var reference = reader.GetTypeReference(handle);
                yield return Reference(reference.Name);
                yield return Reference(reference.Namespace);
            }
            foreach (var handle in reader.FieldDefinitions)
                yield return Reference(reader.GetFieldDefinition(handle).Name, MetadataTokens.GetToken(handle));
            foreach (var handle in reader.MethodDefinitions)
            {
                var method = reader.GetMethodDefinition(handle);
                yield return Reference(method.Name);
                yield return Reference(method.GetImport().Name);
            }
            for (var index = 1; index <= reader.GetTableRowCount(TableIndex.Param); index++)
                yield return Reference(reader.GetParameter(MetadataTokens.ParameterHandle(index)).Name);
            for (var index = 1; index <= reader.GetTableRowCount(TableIndex.GenericParam); index++)
                yield return Reference(reader.GetGenericParameter(MetadataTokens.GenericParameterHandle(index)).Name);
            for (var index = 1; index <= reader.GetTableRowCount(TableIndex.ModuleRef); index++)
                yield return Reference(reader.GetModuleReference(MetadataTokens.ModuleReferenceHandle(index)).Name);
            foreach (var handle in reader.MemberReferences)
                yield return Reference(reader.GetMemberReference(handle).Name);
            foreach (var handle in reader.EventDefinitions)
                yield return Reference(reader.GetEventDefinition(handle).Name);
            foreach (var handle in reader.PropertyDefinitions)
                yield return Reference(reader.GetPropertyDefinition(handle).Name);
            foreach (var handle in reader.AssemblyReferences)
            {
                var reference = reader.GetAssemblyReference(handle);
                yield return Reference(reference.Name);
                yield return Reference(reference.Culture);
            }
            foreach (var handle in reader.AssemblyFiles)
                yield return Reference(reader.GetAssemblyFile(handle).Name);
            foreach (var handle in reader.ExportedTypes)
            {
                var type = reader.GetExportedType(handle);
                yield return Reference(type.Name);
                yield return Reference(type.Namespace);
            }
            foreach (var handle in reader.ManifestResources)
                yield return Reference(reader.GetManifestResource(handle).Name);
        }

        private static StringReference Reference(StringHandle handle, int ownerToken = 0)
            => new StringReference(MetadataTokens.GetHeapOffset(handle), ownerToken);

        private readonly struct StringReference
        {
            internal StringReference(int offset, int ownerToken)
            {
                Offset = offset;
                OwnerToken = ownerToken;
            }
            internal int Offset { get; }
            internal int OwnerToken { get; }
        }
    }
}
