namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;

    public static partial class PowerShellNativeFunctionHost
    {
        /// <summary>Prepares source-locked callbacks while native script dispatch owns authorization and binding.</summary>
        /// <remarks>Call inside the target runspace and invoke the result through ordinary script dispatch. Restricted language modes fail closed.</remarks>
        public static ExternalScriptInfo CreateScriptEntry(SessionState sessionState,
            string sourcePath, string expectedSourceSha256, Action<PowerShellNativeFunctionContext> body)
        {
            if (sessionState == null) throw new ArgumentNullException(nameof(sessionState));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (sessionState.LanguageMode != PSLanguageMode.FullLanguage)
                throw new NotSupportedException("Compiled script callbacks require a FullLanguage invocation.");
            if (expectedSourceSha256 == null || expectedSourceSha256.Length != 64 ||
                !System.Linq.Enumerable.All(expectedSourceSha256, static character =>
                    character >= '0' && character <= '9' || character >= 'a' && character <= 'f'))
                throw new ArgumentException("A canonical SHA256 of the decoded authored source is required.", nameof(expectedSourceSha256));

            var fullPath = System.IO.Path.GetFullPath(sourcePath);
            // Native discovery supplies the execution context, visibility and file policy.
            // Do not use the context-free constructor intended only for authorization managers.
            var info = sessionState.InvokeCommand.GetCommand(fullPath, CommandTypes.ExternalScript) as ExternalScriptInfo
                ?? throw new InvalidOperationException("The compiled script entry could not be discovered as an external script.");
            var source = info.ScriptContents;
            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source)))
                    .Replace("-", string.Empty).ToLowerInvariant();
                if (!string.Equals(hash, expectedSourceSha256, StringComparison.Ordinal))
                    throw new InvalidOperationException("The compiled script entry source differs from its qualified source.");
            }

            // Reparse the exact contents read under native file policy. Authorization
            // remains with ordinary invocation, before metadata/defaults/body execution.
            // A fresh AST has independent compilation data; never patch a cached ScriptBlock clone.
            var ast = ParseCore(source, fullPath, out var errors);
            if (errors.Length != 0) throw new ParseException(errors);
            var script = ast.GetScriptBlock();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var scriptProperty = typeof(ExternalScriptInfo).GetProperty("ScriptBlock", flags)
                ?? throw new NotSupportedException("PowerShell's external script owner is unavailable.");
            scriptProperty.SetValue(info, script, null);
            // The native setter transfers the file's defining mode, including system policy.
            var modeProperty = typeof(ScriptBlock).GetProperty("LanguageMode", flags)
                ?? throw new NotSupportedException("PowerShell's script language-mode contract is unavailable.");
            var mode = modeProperty.GetValue(script, null);
            if (mode != null && (PSLanguageMode)mode != PSLanguageMode.FullLanguage)
                throw new NotSupportedException("Compiled script callbacks do not qualify a restricted file language mode.");
            InstallLazyCompiledClauses(script, null, null, body, Array.Empty<string>());
            return info;
        }
    }
}
