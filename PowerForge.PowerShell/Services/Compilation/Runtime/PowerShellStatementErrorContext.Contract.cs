namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Runtime.ExceptionServices;

    public sealed partial class PowerShellStatementErrorContext
    {
        private sealed class NativeContract
        {
            private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            private static readonly Lazy<NativeContract> Cached = new(() => new NativeContract());
            internal static NativeContract Shared => Cached.Value;

            internal readonly Type CommandRuntimeType;
            internal readonly PropertyInfo CmdletContext, OutputPipe, ErrorOutputPipe, ErrorMergeTo, IsRedirected;
            internal readonly PropertyInfo EngineSessionState, CurrentScope, ShellErrorPipe, PropagateExceptions, InvocationScriptPosition;
            internal readonly PropertyInfo ScopeLocalsTuple;
            internal readonly MethodInfo NewScope, RemoveScope, CheckActionPreference;
            internal readonly MethodInfo MakeTuple, MakeTupleType, SetTupleValue, GetTupleValue, FindMatchingHandler, ConvertToRuntimeException, ConvertToThrownException;
            internal readonly ConstructorInfo FunctionContextConstructor, FunctionInfoConstructor, InvocationInfoConstructor, CommandExceptionConstructor;
            internal readonly MethodInfo ConvertToMethodInvocationException, ConvertToArgumentConversionException;
            internal readonly MethodInfo NewInterpreterException;
            internal readonly MethodInfo FormatOperator;
            internal readonly MethodInfo UnwrapObjectArgument;
            internal readonly MethodInfo CheckEnumerationInterrupts;
            internal readonly MethodInfo SuspendStoppingPipeline, RestoreStoppingPipeline;
            internal readonly MethodInfo AppendErrorToVariables;
            internal readonly PropertyInfo NullInvocationResource;
            internal readonly FieldInfo FunctionExecutionContext, FunctionOutputPipe, FunctionSequencePoints;
            internal readonly FieldInfo CmdletSessionState;
            internal readonly PropertyInfo NativeSessionState;
            internal readonly PropertyInfo ScriptBlockSessionState;
            internal readonly object MergeToOutput;
            internal readonly Type ObjectTupleType, CatchAllType;
            internal readonly bool ThrowConversionTakesRethrow;

            private NativeContract()
            {
                var assembly = typeof(PSObject).Assembly;
                var version = Property(RequireType(assembly, "System.Management.Automation.PSVersionInfo"), "PSVersion", null, isStatic: true).GetValue(null, null)!;
                var major = (int)version.GetType().GetProperty("Major")!.GetValue(version, null)!;
                var minor = (int)version.GetType().GetProperty("Minor")!.GetValue(version, null)!;
                if (!(major == 5 && minor == 1 || major == 7 && (minor == 4 || minor == 6)))
                    throw new NotSupportedException("The loaded PowerShell version is outside the statement-error host profiles.");
                var context = RequireType(assembly, "System.Management.Automation.ExecutionContext");
                CheckEnumerationInterrupts = Method(RequireType(assembly, "System.Management.Automation.PipelineOps"),
                    "CheckForInterrupts", true, typeof(void), context);
                var function = RequireType(assembly, "System.Management.Automation.Language.FunctionContext");
                var pipe = RequireType(assembly, "System.Management.Automation.Internal.Pipe");
                var state = RequireType(assembly, "System.Management.Automation.SessionStateInternal");
                var scope = RequireType(assembly, "System.Management.Automation.SessionStateScope");
                var errors = RequireType(assembly, "System.Management.Automation.ExceptionHandlingOps");
                SuspendStoppingPipeline = Method(errors, "SuspendStoppingPipeline", true, typeof(bool), context);
                RestoreStoppingPipeline = Method(errors, "RestoreStoppingPipeline", true, typeof(void), context, typeof(bool));
                FormatOperator = Method(RequireType(assembly, "System.Management.Automation.StringOps"),
                    "FormatOperator", true, typeof(string), typeof(string), typeof(object));
                UnwrapObjectArgument = Method(typeof(PSObject), "Base", true, typeof(object), typeof(object));
                var tuple = RequireType(assembly, "System.Management.Automation.MutableTuple");
                ObjectTupleType = RequireType(assembly, "System.Management.Automation.MutableTuple`1").MakeGenericType(typeof(object));
                CatchAllType = RequireType(assembly, "System.Management.Automation.ExceptionHandlingOps+CatchAll");
                CommandRuntimeType = RequireType(assembly, "System.Management.Automation.MshCommandRuntime");
                CmdletContext = Property(typeof(Cmdlet), "Context", context);
                OutputPipe = Property(CommandRuntimeType, "OutputPipe", pipe);
                ErrorOutputPipe = Property(CommandRuntimeType, "ErrorOutputPipe", pipe);
                ErrorMergeTo = Property(CommandRuntimeType, "ErrorMergeTo", null);
                MergeToOutput = Enum.Parse(ErrorMergeTo.PropertyType, "Output");
                IsRedirected = Property(pipe, "IsRedirected", typeof(bool));
                EngineSessionState = Property(context, "EngineSessionState", state, writable: true);
                NativeSessionState = Property(typeof(SessionState), "Internal", state);
                ScriptBlockSessionState = Property(typeof(ScriptBlock), "SessionStateInternal", state, writable: true);
                CmdletSessionState = Field(RequireType(assembly, "System.Management.Automation.Internal.InternalCommand"),
                    major == 5 ? "state" : "_state", typeof(SessionState));
                CurrentScope = Property(state, "CurrentScope", scope, writable: true);
                ScopeLocalsTuple = Property(scope, "LocalsTuple", tuple, writable: true);
                ShellErrorPipe = Property(context, "ShellFunctionErrorOutputPipe", pipe, writable: true);
                PropagateExceptions = Property(context, "PropagateExceptionsToEnclosingStatementBlock", typeof(bool), writable: true);
                InvocationScriptPosition = Property(typeof(InvocationInfo), "ScriptPosition", typeof(IScriptExtent));
                NewScope = Method(state, "NewScope", false, scope, typeof(bool));
                RemoveScope = Method(state, "RemoveScope", false, typeof(void), scope);
                AppendErrorToVariables = Method(CommandRuntimeType, "AppendErrorToVariables", false, typeof(void), typeof(object));
                CheckActionPreference = Method(errors, "CheckActionPreference", true, typeof(void), function, typeof(Exception));
                ConvertToRuntimeException = Method(errors, "ConvertToRuntimeException", true, typeof(RuntimeException), typeof(Exception), typeof(IScriptExtent));
                ThrowConversionTakesRethrow = major == 7;
                ConvertToThrownException = ThrowConversionTakesRethrow
                    ? Method(errors, "ConvertToException", true, typeof(RuntimeException), typeof(object), typeof(IScriptExtent), typeof(bool))
                    : Method(errors, "ConvertToException", true, typeof(RuntimeException), typeof(object), typeof(IScriptExtent));
                FindMatchingHandler = Method(errors, "FindMatchingHandler", true, typeof(int), tuple, typeof(RuntimeException), typeof(Type[]), context);
                SetTupleValue = Method(tuple, "SetValue", false, typeof(void), typeof(int), typeof(object));
                GetTupleValue = Method(tuple, "GetValue", false, typeof(object), typeof(int));
                MakeTuple = Method(tuple, "MakeTuple", true, tuple,
                    typeof(Type), typeof(Dictionary<string, int>), typeof(Func<>).MakeGenericType(tuple));
                MakeTupleType = Method(tuple, "MakeTupleType", true, typeof(Type), typeof(Type[]));
                FunctionContextConstructor = Constructor(function);
                FunctionInfoConstructor = Constructor(typeof(FunctionInfo), typeof(string), typeof(ScriptBlock), context);
                InvocationInfoConstructor = Constructor(typeof(InvocationInfo), typeof(CommandInfo), typeof(IScriptExtent), context);
                CommandExceptionConstructor = Constructor(typeof(CmdletInvocationException), typeof(Exception), typeof(InvocationInfo));
                ConvertToMethodInvocationException = Method(errors, "ConvertToMethodInvocationException", true, typeof(void),
                    typeof(Exception), typeof(Type), typeof(string), typeof(int), typeof(MemberInfo));
                ConvertToArgumentConversionException = Method(errors, "ConvertToArgumentConversionException", true, typeof(void),
                    typeof(Exception), typeof(string), typeof(object), typeof(string), typeof(Type));
                NewInterpreterException = Method(RequireType(assembly, "System.Management.Automation.InterpreterError"),
                    "NewInterpreterException", true, typeof(RuntimeException), typeof(object), typeof(Type), typeof(IScriptExtent),
                    typeof(string), typeof(string), typeof(object[]));
                NullInvocationResource = Property(RequireType(assembly, "ParserStrings"), "InvokeMethodOnNull", typeof(string), isStatic: true);
                FunctionExecutionContext = Field(function, "_executionContext", context);
                FunctionOutputPipe = Field(function, "_outputPipe", pipe);
                FunctionSequencePoints = Field(function, "_sequencePoints", typeof(IScriptExtent[]));
            }

            internal object GetRequired(PropertyInfo property, object? instance)
                => property.GetValue(instance, null) ?? throw Unavailable(property.DeclaringType!, property.Name);

            internal object CreateTuple(string variableName, object? value)
            {
                var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [variableName] = 0 };
                var tuple = Invoke(MakeTuple, null, ObjectTupleType, names, null)!;
                Invoke(SetTupleValue, tuple, 0, value);
                return tuple;
            }

            internal object CreateTuple(IReadOnlyDictionary<string, object?> values)
            {
                var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var types = new Type[values.Count];
                var index = 0;
                foreach (var name in values.Keys)
                {
                    names.Add(name, index);
                    types[index++] = typeof(object);
                }
                var type = Invoke(MakeTupleType, null, (object)types)!;
                var tuple = Invoke(MakeTuple, null, type, names, null)!;
                foreach (var value in values) Invoke(SetTupleValue, tuple, names[value.Key], value.Value);
                return tuple;
            }

            private static Type RequireType(Assembly assembly, string name)
                => assembly.GetType(name, throwOnError: false) ?? throw new NotSupportedException(
                    "The PowerShell host does not provide the statement-error contract type '" + name + "'.");

            private static PropertyInfo Property(Type owner, string name, Type? valueType, bool writable = false, bool isStatic = false)
            {
                var property = owner.GetProperty(name, isStatic ? Static : Instance);
                if (property is null || property.GetMethod is null ||
                    valueType is not null && property.PropertyType != valueType || writable && property.SetMethod is null)
                    throw Unavailable(owner, name);
                return property;
            }

            private static FieldInfo Field(Type owner, string name, Type valueType)
            {
                var field = owner.GetField(name, Instance);
                if (field is null || field.FieldType != valueType) throw Unavailable(owner, name);
                return field;
            }

            private static MethodInfo Method(Type owner, string name, bool isStatic, Type result, params Type[] arguments)
            {
                var method = owner.GetMethod(name, isStatic ? Static : Instance, null, arguments, null);
                if (method is null || method.ReturnType != result) throw Unavailable(owner, name);
                return method;
            }

            private static ConstructorInfo Constructor(Type owner, params Type[] arguments)
                => owner.GetConstructor(Instance, null, arguments, null) ?? throw Unavailable(owner, ".ctor");

            private static NotSupportedException Unavailable(Type owner, string member)
                => new("The PowerShell host does not provide the qualified statement-error contract '" +
                    owner.FullName + "." + member + "'.");

            internal static object Construct(ConstructorInfo constructor, params object?[] arguments)
            {
                try { return constructor.Invoke(arguments); }
                catch (TargetInvocationException error) when (error.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                    throw;
                }
            }

            internal static object? Invoke(MethodInfo method, object? instance, params object?[] arguments)
            {
                try { return method.Invoke(instance, arguments); }
                catch (TargetInvocationException error) when (error.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                    throw;
                }
            }
        }
    }
}
