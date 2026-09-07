namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.ExceptionServices;

    /// <summary>Uses the loaded host's script string-parameter conversion before binary command validation.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PowerShellStringParameterAttribute : ArgumentTransformationAttribute
    {
        private readonly bool _advancedFunction;

        /// <summary>Creates a conversion with the authored function's array-to-string binding policy.</summary>
        public PowerShellStringParameterAttribute(bool advancedFunction) => _advancedFunction = advancedFunction;

        /// <summary>Preserves null normalization, reference dereferencing, conversion errors, and host input tracking.</summary>
        public override object Transform(EngineIntrinsics engineIntrinsics, object inputData)
        {
            var contract = NativeContract.Shared;
            try
            {
                return contract.Transform.Invoke(contract.Converter,
                    new object?[] { engineIntrinsics, inputData, true, _advancedFunction })!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private sealed class NativeContract
        {
            private static readonly Lazy<NativeContract> Cached = new(() => new NativeContract());
            internal static NativeContract Shared => Cached.Value;
            internal readonly object Converter;
            internal readonly MethodInfo Transform;

            private NativeContract()
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var assembly = typeof(PSObject).Assembly;
                var version = assembly.GetType("System.Management.Automation.PSVersionInfo")?
                    .GetProperty("PSVersion", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
                var major = version?.GetType().GetProperty("Major")?.GetValue(version, null) as int?;
                var minor = version?.GetType().GetProperty("Minor")?.GetValue(version, null) as int?;
                if (!(major == 5 && minor == 1 || major == 7 && (minor == 4 || minor == 6)))
                    throw new NotSupportedException("The loaded PowerShell version is outside the script string-parameter host profiles.");
                var type = assembly.GetType("System.Management.Automation.ArgumentTypeConverterAttribute")
                    ?? throw new NotSupportedException("The loaded PowerShell host has no script argument converter.");
                if (!typeof(ArgumentTransformationAttribute).IsAssignableFrom(type))
                    throw new NotSupportedException("The loaded PowerShell script argument converter has an incompatible base type.");
                var constructor = type.GetConstructor(flags, null, new[] { typeof(Type[]) }, null)
                    ?? throw new NotSupportedException("The loaded PowerShell script argument converter has an incompatible constructor.");
                Transform = type.GetMethod("Transform", flags, null,
                    new[] { typeof(EngineIntrinsics), typeof(object), typeof(bool), typeof(bool) }, null)
                    ?? throw new NotSupportedException("The loaded PowerShell script argument converter has an incompatible binding contract.");
                if (Transform.ReturnType != typeof(object))
                    throw new NotSupportedException("The loaded PowerShell script argument converter has an incompatible result contract.");
                Converter = constructor.Invoke(new object[] { new[] { typeof(string) } });
            }
        }
    }
}
