using System.Reflection;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static string GetAnnotatedTypeName(Type type, object member, NullabilityInfoContext? context, bool qualified = false)
    {
        NullabilityInfo? info = null;
        if (context is not null)
        {
            try
            {
                info = member switch
                {
                    ParameterInfo parameter => context.Create(parameter),
                    PropertyInfo property => context.Create(property),
                    FieldInfo field => context.Create(field),
                    EventInfo evt => context.Create(evt),
                    _ => null
                };
            }
            catch (InvalidOperationException) { /* Assemblies can omit nullability metadata. */ }
            catch (NotSupportedException) { /* Some reflection providers cannot expose annotations. */ }
        }
        return GetAnnotatedTypeName(type, info, qualified);
    }

    private static string GetAnnotatedTypeName(Type type, NullabilityInfo? info, bool qualified = false)
    {
        if (type.IsByRef) type = type.GetElementType() ?? type;
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return GetAnnotatedTypeName(underlying, info?.GenericTypeArguments.FirstOrDefault(), qualified) + "?";

        string name;
        if (type.IsArray)
        {
            name = GetAnnotatedTypeName(type.GetElementType() ?? typeof(object), info?.ElementType, qualified) +
                "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }
        else if (type.IsGenericType)
        {
            var args = type.GetGenericArguments().Select((argument, index) =>
                GetAnnotatedTypeName(argument, info is not null && index < info.GenericTypeArguments.Length
                    ? info.GenericTypeArguments[index] : null, qualified));
            name = StripGenericArity(qualified ? type.GetGenericTypeDefinition().FullName?.Replace('+', '.') ?? type.Name : type.Name) + "<" + string.Join(", ", args) + ">";
        }
        else name = qualified ? type.FullName?.Replace('+', '.') ?? type.Name : type.Name;

        var state = info?.ReadState == NullabilityState.Unknown ? info.WriteState : info?.ReadState;
        return !type.IsValueType && state == NullabilityState.Nullable ? name + "?" : name;
    }
}
