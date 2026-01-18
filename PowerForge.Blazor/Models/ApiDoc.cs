namespace PowerForge.Blazor;

/// <summary>
/// API documentation extracted from C# XML documentation files.
/// </summary>
public class ApiDoc
{
    /// <summary>
    /// Assembly name.
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// Assembly version.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// All documented namespaces.
    /// </summary>
    public List<ApiNamespace> Namespaces { get; set; } = new();

    /// <summary>
    /// All documented types (flat list for searching).
    /// </summary>
    public List<ApiType> Types { get; set; } = new();
}

/// <summary>
/// A documented namespace.
/// </summary>
public class ApiNamespace
{
    /// <summary>
    /// Namespace name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Summary documentation.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Types in this namespace.
    /// </summary>
    public List<ApiType> Types { get; set; } = new();
}

/// <summary>
/// A documented type (class, struct, interface, enum, delegate).
/// </summary>
public class ApiType
{
    /// <summary>
    /// Type name (without namespace).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full type name including namespace.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Namespace this type belongs to.
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>
    /// Type kind (Class, Struct, Interface, Enum, Delegate).
    /// </summary>
    public ApiTypeKind Kind { get; set; }

    /// <summary>
    /// Summary documentation.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Remarks documentation.
    /// </summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// Example code.
    /// </summary>
    public string? Example { get; set; }

    /// <summary>
    /// See-also references.
    /// </summary>
    public List<string> SeeAlso { get; set; } = new();

    /// <summary>
    /// Base type (if any).
    /// </summary>
    public string? BaseType { get; set; }

    /// <summary>
    /// Implemented interfaces.
    /// </summary>
    public List<string> Interfaces { get; set; } = new();

    /// <summary>
    /// Generic type parameters.
    /// </summary>
    public List<ApiTypeParam> TypeParameters { get; set; } = new();

    /// <summary>
    /// Type modifiers (public, abstract, sealed, static).
    /// </summary>
    public List<string> Modifiers { get; set; } = new();

    /// <summary>
    /// Constructors.
    /// </summary>
    public List<ApiMethod> Constructors { get; set; } = new();

    /// <summary>
    /// Properties.
    /// </summary>
    public List<ApiProperty> Properties { get; set; } = new();

    /// <summary>
    /// Methods.
    /// </summary>
    public List<ApiMethod> Methods { get; set; } = new();

    /// <summary>
    /// Fields.
    /// </summary>
    public List<ApiField> Fields { get; set; } = new();

    /// <summary>
    /// Events.
    /// </summary>
    public List<ApiEvent> Events { get; set; } = new();

    /// <summary>
    /// Enum values (for enums only).
    /// </summary>
    public List<ApiEnumValue> EnumValues { get; set; } = new();

    /// <summary>
    /// Nested types.
    /// </summary>
    public List<ApiType> NestedTypes { get; set; } = new();
}

/// <summary>
/// Type kinds.
/// </summary>
public enum ApiTypeKind
{
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
    Record
}

/// <summary>
/// A generic type parameter.
/// </summary>
public class ApiTypeParam
{
    /// <summary>
    /// Parameter name (e.g., "T").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description from typeparam tag.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Constraints (e.g., "class", "new()", "IDisposable").
    /// </summary>
    public List<string> Constraints { get; set; } = new();
}

/// <summary>
/// A method or constructor.
/// </summary>
public class ApiMethod
{
    /// <summary>
    /// Method name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Summary documentation.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Remarks documentation.
    /// </summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// Return type.
    /// </summary>
    public string ReturnType { get; set; } = "void";

    /// <summary>
    /// Return value documentation.
    /// </summary>
    public string? Returns { get; set; }

    /// <summary>
    /// Parameters.
    /// </summary>
    public List<ApiParameter> Parameters { get; set; } = new();

    /// <summary>
    /// Generic type parameters.
    /// </summary>
    public List<ApiTypeParam> TypeParameters { get; set; } = new();

    /// <summary>
    /// Exceptions that may be thrown.
    /// </summary>
    public List<ApiException> Exceptions { get; set; } = new();

    /// <summary>
    /// Example code.
    /// </summary>
    public string? Example { get; set; }

    /// <summary>
    /// Method modifiers (public, static, virtual, override, async).
    /// </summary>
    public List<string> Modifiers { get; set; } = new();

    /// <summary>
    /// Whether this is an extension method.
    /// </summary>
    public bool IsExtensionMethod { get; set; }

    /// <summary>
    /// Whether this is a constructor.
    /// </summary>
    public bool IsConstructor { get; set; }

    /// <summary>
    /// Method signature for display.
    /// </summary>
    public string? Signature { get; set; }
}

/// <summary>
/// A method parameter.
/// </summary>
public class ApiParameter
{
    /// <summary>
    /// Parameter name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parameter type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Description from param tag.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Default value (if optional).
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Parameter modifiers (ref, out, in, params).
    /// </summary>
    public string? Modifier { get; set; }

    /// <summary>
    /// Whether this parameter is optional.
    /// </summary>
    public bool IsOptional { get; set; }
}

/// <summary>
/// A property.
/// </summary>
public class ApiProperty
{
    /// <summary>
    /// Property name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Property type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Summary documentation.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Remarks documentation.
    /// </summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// Value documentation.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Whether the property has a getter.
    /// </summary>
    public bool HasGetter { get; set; } = true;

    /// <summary>
    /// Whether the property has a setter.
    /// </summary>
    public bool HasSetter { get; set; } = true;

    /// <summary>
    /// Property modifiers.
    /// </summary>
    public List<string> Modifiers { get; set; } = new();

    /// <summary>
    /// Default value (for auto-properties).
    /// </summary>
    public string? DefaultValue { get; set; }
}

/// <summary>
/// A field.
/// </summary>
public class ApiField
{
    /// <summary>
    /// Field name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Field type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Summary documentation.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Field modifiers (const, readonly, static).
    /// </summary>
    public List<string> Modifiers { get; set; } = new();

    /// <summary>
    /// Constant/default value.
    /// </summary>
    public string? Value { get; set; }
}

/// <summary>
/// An event.
/// </summary>
public class ApiEvent
{
    /// <summary>
    /// Event name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Event handler type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Summary documentation.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Event modifiers.
    /// </summary>
    public List<string> Modifiers { get; set; } = new();
}

/// <summary>
/// An enum value.
/// </summary>
public class ApiEnumValue
{
    /// <summary>
    /// Enum member name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Numeric value.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Summary documentation.
    /// </summary>
    public string? Summary { get; set; }
}

/// <summary>
/// An exception that a method may throw.
/// </summary>
public class ApiException
{
    /// <summary>
    /// Exception type name.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Condition/description.
    /// </summary>
    public string? Description { get; set; }
}
