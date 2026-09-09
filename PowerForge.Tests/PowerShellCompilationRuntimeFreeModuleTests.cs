namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("Seed")]
    [InlineData("previous")]
    [InlineData("__moduleBindings")]
    [InlineData("__previousModuleState")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_DefaultOverloadsAndValidationPreserveState(string parameterName)
    {
        using var fixture = ArtifactFixture.Create(RuntimeFreeCounterModule.Replace("param([int]$Seed,[bool]$Fail)",
            "param([ValidateRange(-100,100)][int]$Seed = 7,[bool]$Fail = $false)", StringComparison.Ordinal)
            .Replace("$Seed", "$" + parameterName, StringComparison.Ordinal), ".psm1");
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "RuntimeFreeDefaultCounter", PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.Equal(new[] { 0, 1, 2 }, build.Manifest!.PublicAbi!.ModuleLifetime!.SupportedParameterCounts);
        var type = System.Reflection.Assembly.LoadFile(build.ArtifactPath!).GetType(
            build.Manifest.PublicAbi.NamespaceName + "." + build.Manifest.PublicAbi.TypeName)!;
        using var omitted = (IDisposable)Activator.CreateInstance(type)!;
        using var partial = (IDisposable)Activator.CreateInstance(type, new object[] { 9 })!;
        using var explicitZero = (IDisposable)Activator.CreateInstance(type, new object[] { 0, false })!;
        var get = type.GetMethod("Get_Counter")!;
        Assert.Equal(7, get.Invoke(omitted, null));
        Assert.Equal(9, get.Invoke(partial, null));
        Assert.Equal(0, get.Invoke(explicitZero, null));
        type.GetMethod("Reset", new[] { typeof(int) })!.Invoke(omitted, new object[] { 40 });
        Assert.Equal(40, get.Invoke(omitted, null));
        type.GetMethod("Reset", Type.EmptyTypes)!.Invoke(omitted, null);
        Assert.Equal(7, get.Invoke(omitted, null));
        var reset = type.GetMethod("Reset", new[] { typeof(int), typeof(bool) })!;
        var invalid = Assert.Throws<System.Reflection.TargetInvocationException>(() => reset.Invoke(omitted, new object[] { 500, false }));
        Assert.IsType<ArgumentException>(invalid.InnerException);
        Assert.Equal(7, get.Invoke(omitted, null));
        var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => reset.Invoke(omitted, new object[] { 8, true }));
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal(7, get.Invoke(omitted, null));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("PID")]
    [InlineData("HOME")]
    [InlineData("null")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_RejectsAutomaticFieldDeclarations(string name)
    {
        using var fixture = ArtifactFixture.Create("[int]$script:" + name + " = 1\nfunction Get-Value { param() return $script:" + name + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, "Generated", "Counter", "net10.0");
        Assert.Contains(typed.Diagnostics, diagnostic => diagnostic.Message.Contains("cannot define managed module fields", StringComparison.Ordinal));
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath,
            PowerShellCompilationMode.Strict, targetFramework: "net10.0", capabilities: PowerShellCompilationCapabilities.TypedLibrary));
        Assert.False(plan.CanProceed);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_DoesNotDiscardShadowedExportCommand()
    {
        using var fixture = ArtifactFixture.Create("""
            [int]$script:Count = 1
            function Export-ModuleMember { param([string]$Function) $script:Count = 7 }
            function Get-Counter { [OutputType([int])] param() return $script:Count }
            Export-ModuleMember -Function 'Get-Counter'
            """, ".psm1");
        var definition = PowerShellRuntimeFreeModuleDefinition.Discover(new[] { PowerShellSourceParser.ParseFile(fixture.ScriptPath) },
            new List<PowerShellSemanticDiagnostic>());
        Assert.NotNull(definition);
        Assert.Equal(2, definition.Initializer.Body.EndBlock.Statements.Count);
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, "Generated", "Counter", "net10.0");
        Assert.Contains(typed.Diagnostics, diagnostic => diagnostic.Message.Contains("Initialization calls into module functions", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_StrictArtifactIncludesInitializationAndInstanceAbi(string targetFramework)
    {
        using var fixture = ArtifactFixture.Create(RuntimeFreeCounterModule, ".psm1");
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "RuntimeFreeCounter", PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = targetFramework });
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var manifest = build.Manifest!;
        Assert.False(manifest.RequiresPowerShellRuntime);
        Assert.False(manifest.UsesPowerShellRuntimeFallback);
        Assert.True(manifest.DependencyClosureVerified);
        Assert.NotNull(manifest.DependencyClosure);
        Assert.True(manifest.DependencyClosure.Verified);
        Assert.Empty(manifest.DependencyClosure.Limitations);
        Assert.Equal(0, manifest.RuntimeFallbackUnits);
        Assert.Equal(0, manifest.OmittedUnits);
        Assert.Equal(4, manifest.CompiledMethods);
        Assert.Equal(5, manifest.PublicAbi!.SchemaVersion);
        Assert.Equal("5", manifest.SemanticProfile!.CompilerRuntimeAbiVersion);
        Assert.Equal(3, manifest.PublicAbi.Methods.Length);
        Assert.All(manifest.PublicAbi.Methods, method => Assert.True(method.IsInstanceMethod));
        Assert.Equal("AtomicRollback", manifest.PublicAbi.ModuleLifetime!.ResetPolicy);
        var serializedAbi = System.Text.Json.JsonSerializer.Serialize(manifest.PublicAbi);
        var restoredAbi = System.Text.Json.JsonSerializer.Deserialize<PowerShellCompilationAbiManifest>(serializedAbi)!;
        Assert.Equal(manifest.PublicAbi.ModuleLifetime.Fields.Select(field => field.Name),
            restoredAbi.ModuleLifetime!.Fields.Select(field => field.Name));
        Assert.Equal(manifest.PublicAbi.Sha256,
            PowerShellCompilationAbiBuilder.ComputeSha256(PowerShellCompilationAbiBuilder.GetNormalizedText(restoredAbi)));
        var assembly = System.Reflection.Assembly.LoadFile(build.ArtifactPath!);
        var type = assembly.GetType(manifest.PublicAbi.NamespaceName + "." + manifest.PublicAbi.TypeName)!;
        using var instance = (IDisposable)Activator.CreateInstance(type, new object[] { 42, false })!;
        Assert.Equal(42, type.GetMethod("Get_Counter")!.Invoke(instance, null));
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "System.Management.Automation");
        VerifyRuntimeFreeModuleArtifactConsumer(fixture.RootPath, fixture.ScriptPath, build, targetFramework);
    }

    private static void VerifyRuntimeFreeModuleArtifactConsumer(string root, string sourcePath, PowerShellCompilationBuildResult artifact, string targetFramework)
    {
        var directory = Path.Combine(root, "artifact-consumer");
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "ModuleConsumer.csproj");
        new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("TargetFramework", targetFramework),
                new System.Xml.Linq.XElement("OutputType", "Exe"), new System.Xml.Linq.XElement("ImplicitUsings", "enable")),
            new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("Reference", new System.Xml.Linq.XAttribute("Include", "RuntimeFreeCounter"),
                new System.Xml.Linq.XElement("HintPath", artifact.ArtifactPath!))))).Save(project);
        var abi = artifact.Manifest!.PublicAbi!;
        File.WriteAllText(Path.Combine(directory, "Program.cs"), "using Counter = global::" + abi.NamespaceName + "." + abi.TypeName + ";\n" + """
            using var first = new Counter(10, false);
            using var second = new Counter(100, false);
            Parallel.For(0, 1000, _ => first.Add_Counter(1));
            if (first.Get_Counter() != 1010 || second.Get_Counter() != 100) return 1;
            try { first.Reset(500, true); return 2; } catch (InvalidOperationException) { }
            if (first.Get_Counter() != 1010 || first.Get_Label() != "counter") return 3;
            first.Reset(20, false);
            if (first.Get_Counter() != 20) return 4;
            try { using var failed = new Counter(1, true); return 5; } catch (InvalidOperationException) { }
            first.Dispose(); first.Dispose();
            try { first.Get_Counter(); return 6; } catch (ObjectDisposedException) { }
            try { first.Add_Counter(1); return 7; } catch (ObjectDisposedException) { }
            try { first.Reset(1, false); return 8; } catch (ObjectDisposedException) { }
            if (second.Get_Counter() != 100) return 9;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Management.Automation")) return 10;
            foreach (var seed in new[] { -10, 0, 10 }) {
                using var observed = new Counter(seed, false);
                Console.WriteLine("observed:" + seed + ":" + observed.Add_Counter(2) + ":" + observed.Add_Counter(-1) + ":" + observed.Get_Counter() + ":" + observed.Get_Label());
            }
            Console.WriteLine("independent module artifact consumer passed");
            return 0;
            """);
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "--nologo", "-v:q");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var run = RunProcess("dotnet", Path.Combine(directory, "bin", "Release", targetFramework, "ModuleConsumer.dll"));
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError + " exit=" + run.ExitCode);
        Assert.Contains("independent module artifact consumer passed", run.StandardOutput, StringComparison.Ordinal);
        var compiledObservations = run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.StartsWith("observed:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, compiledObservations.Length);
        foreach (var configuration in StatementErrorHosts())
        {
            var original = RunStatementErrorProbe((string)configuration[1],
                "$modulePath='" + EscapeStatementErrorPath(sourcePath) + "'; " + """
                foreach($seed in -10,0,10) {
                    $module = Import-Module $modulePath -ArgumentList $seed,$false -PassThru -Force -DisableNameChecking
                    'observed:' + $seed + ':' + (Add-Counter 2) + ':' + (Add-Counter -1) + ':' + (Get-Counter) + ':' + (Get-Label)
                    Remove-Module $module
                }
                """, root, "original-managed-state-" + configuration[0]);
            Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
            Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
            Assert.Equal(compiledObservations, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }

    [Theory]
    [InlineData("Counter")]
    [InlineData("__ModuleState")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_GeneratedInstanceExecutesLifetimeWithoutPowerShell(string requestedTypeName)
    {
        using var fixture = ArtifactFixture.Create(RuntimeFreeCounterModule + "\n" +
            "[int]$script:__ModuleState = 17\n" +
            "function Get-InternalName { [OutputType([int])] param() return $script:__ModuleState }\n" +
            "function Set-Last { [OutputType([int])] param([int[]]$Values) foreach ($script:Count in $Values) {} return $script:Count }\n" +
            "function Get-Inherited { [OutputType([int])] param() return $Count }\n" +
            "function Get-Shadow { [OutputType([int])] param([int]$Count) return $Count }\n" +
            "function Get-LocalShadow { [OutputType([int])] param() [int]$Count = 99; return $Count }\n" +
            "function Invoke-Callback { param() Write-Verbose 'running'; $script:Count++ }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath,
            "Generated", requestedTypeName, "net10.0");
        Assert.Empty(typed.Diagnostics);
        Assert.NotNull(typed.RuntimeFreeModule);
        Assert.Single(typed.Methods, method => method.IsModuleInitializer);
        Assert.All(typed.Methods, method => Assert.True(method.IsInstanceMethod));
        var directory = Path.Combine(fixture.RootPath, "instance-consumer");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Counter.cs"), typed.SourceCode);
        var project = Path.Combine(directory, "InstanceConsumer.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>
            """);
        File.WriteAllText(Path.Combine(directory, "Program.cs"), "using Counter = global::Generated." + typed.TypeName + ";\n" + """
            using var first = new Counter(10, false);
            using var second = new Counter(100, false);
            if (first.Get_InternalName() != 17) return 15;
            if (first.Add_Counter(2) != 12 || second.Get_Counter() != 100 || first.Get_Label() != "counter") return 1;
            Parallel.For(0, 1000, _ => first.Add_Counter(1));
            if (first.Get_Counter() != 1012) return 2;
            try { first.Reset(500, true); return 3; } catch (InvalidOperationException) { }
            if (first.Get_Counter() != 1012) return 4;
            first.Reset(20, false);
            if (first.Get_Counter() != 20 || second.Get_Counter() != 100) return 5;
            if (first.Set_Last(new[] { 30, 40 }) != 40 || first.Get_Counter() != 40) return 12;
            if (first.Get_Inherited() != 40 || first.Get_Shadow(88) != 88 || first.Get_LocalShadow() != 99 || first.Get_Counter() != 40) return 14;
            var resetRejected = false; var disposeRejected = false;
            first.Invoke_Callback(__writeOutput: _ => {}, __writeDebug: _ => {}, __writeWarning: _ => {},
                __writeInformation: _ => {}, __writeHost: _ => {}, __writeError: _ => {}, __writeVerbose: _ => {
                try { first.Reset(500, false); } catch (InvalidOperationException) { resetRejected = true; }
                try { first.Dispose(); } catch (InvalidOperationException) { disposeRejected = true; }
                first.Add_Counter(1);
            });
            if (!resetRejected || !disposeRejected || first.Get_Counter() != 42) return 13;
            try { using var failed = new Counter(1, true); return 6; } catch (InvalidOperationException) { }
            first.Dispose(); first.Dispose();
            try { first.Get_Counter(); return 7; } catch (ObjectDisposedException) { }
            try { first.Add_Counter(1); return 8; } catch (ObjectDisposedException) { }
            try { first.Reset(1, false); return 9; } catch (ObjectDisposedException) { }
            if (second.Get_Counter() != 100) return 10;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Management.Automation")) return 11;
            Console.WriteLine("runtime-free instance lifetime passed");
            return 0;
            """);
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "--nologo", "-v:q");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var run = RunProcess("dotnet", Path.Combine(directory, "bin", "Release", "net10.0", "InstanceConsumer.dll"));
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError + " exit=" + run.ExitCode);
        Assert.Contains("runtime-free instance lifetime passed", run.StandardOutput, StringComparison.Ordinal);
    }

    private const string RuntimeFreeCounterModule = """
        param([int]$Seed,[bool]$Fail)
        [int]$script:Count = $Seed
        [string]$script:Label = 'counter'
        if ($Fail) { throw [InvalidOperationException]::new('initialization failed') }
        function Add-Counter { [OutputType([int])] param([int]$Value) $script:Count += $Value; return $script:Count }
        function Get-Counter { [OutputType([int])] param() return $script:Count }
        function Get-Label { [OutputType([string])] param() return $script:Label }
        Export-ModuleMember -Function Add-Counter, Get-Counter, Get-Label
        """;

    [Theory]
    [InlineData("function Invoke-Shadow { [OutputType([int])] param([int]$Count) return Get-Inherited }")]
    [InlineData("function Invoke-Shadow { [OutputType([int])] param() [int]$Count = 88; return Get-Inherited }")]
    [InlineData("function Invoke-Shadow { [OutputType([int])] param([int]$Count) return Invoke-Middle }; function Invoke-Middle { [OutputType([int])] param() return Get-Inherited }")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_RejectsCallerShadowedInheritedReads(string caller)
    {
        using var fixture = ArtifactFixture.Create("[int]$script:Count = 40\n" +
            "function Get-Inherited { [OutputType([int])] param() return $Count }\n" + caller, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, "Generated", "Counter", "net10.0");
        Assert.Contains(typed.Diagnostics, diagnostic => diagnostic.Message.Contains("can be shadowed by caller", StringComparison.Ordinal));
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath,
            PowerShellCompilationMode.Strict, targetFramework: "net10.0", capabilities: PowerShellCompilationCapabilities.TypedLibrary));
        Assert.False(plan.CanProceed);
    }

    [Theory]
    [InlineData("Export-ModuleMember -Function (Get-ExportNames)")]
    [InlineData("Export-ModuleMember -Function $Names")]
    [InlineData(". (Get-ModulePath)")]
    [InlineData(". './other.ps1'")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_PreservesExecutableDirectivesForBinding(string directive)
    {
        using var fixture = ArtifactFixture.Create("[int]$script:Count = 1\n" + directive, ".psm1");
        var document = PowerShellSourceParser.ParseFile(fixture.ScriptPath);
        var diagnostics = new List<PowerShellSemanticDiagnostic>();
        var definition = PowerShellRuntimeFreeModuleDefinition.Discover(new[] { document }, diagnostics);
        Assert.NotNull(definition);
        Assert.Equal(2, definition.Initializer.Body.EndBlock.Statements.Count);
        Assert.Equal(directive, definition.Initializer.Body.EndBlock.Statements[1].Extent.Text);
    }

    [Theory]
    [InlineData("[int[]]$Seed")]
    [InlineData("[object]$Seed")]
    [InlineData("$Seed")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_RejectsMutableOrUntypedConstructorParameters(string parameter)
    {
        using var fixture = ArtifactFixture.Create("param(" + parameter + ")\n[int]$script:Count = 1", ".psm1");
        var diagnostics = new List<PowerShellSemanticDiagnostic>();
        PowerShellRuntimeFreeModuleDefinition.Discover(new[] { PowerShellSourceParser.ParseFile(fixture.ScriptPath) }, diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "PSB2950" &&
            diagnostic.Message.Contains("constructor parameters", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_BindsInitializerAndFieldsThroughCanonicalPipeline()
    {
        using var fixture = ArtifactFixture.Create(RuntimeFreeCounterModule, ".psm1");
        var semantic = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.ParseFile(fixture.ScriptPath) }, "net10.0", PowerShellCompilationCapabilities.TypedLibrary);
        Assert.True(semantic.Lowered.Diagnostics.Length == 0,
            string.Join(Environment.NewLine, semantic.Lowered.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        Assert.NotNull(semantic.RuntimeFreeModule);
        Assert.Equal(4, semantic.Emitted.Methods.Length);
        Assert.Equal(2, semantic.RuntimeFreeModule.Fields.Length);
        Assert.All(semantic.Lowered.Functions, function => Assert.False(function.RequiresPowerShellModuleState));
        Assert.Contains(semantic.Emitted.Methods, method => method.Source.Contains("this.__moduleState.__field_Count", StringComparison.Ordinal));
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath,
            PowerShellCompilationMode.Strict, targetFramework: "net10.0", capabilities: PowerShellCompilationCapabilities.TypedLibrary));
        Assert.True(plan.CanProceed, string.Join(Environment.NewLine, plan.Files.SelectMany(file => file.Units)
            .SelectMany(unit => unit.Diagnostics).Select(diagnostic => diagnostic.Message)));
    }

    [Theory]
    [InlineData("[DateTime]$script:When = [DateTime]::Now")]
    [InlineData("[Guid]$script:Identity = [Guid]::NewGuid()")]
    [InlineData("[int]$script:Identity = $PID")]
    [InlineData("[string]$script:HomeValue = $HOME")]
    [InlineData("[int]$script:Count = 1; return")]
    [InlineData("[int]$script:Count = Get-Seed; function Get-Seed { [OutputType([int])] param() return 1 }")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeFreeModule_RejectsUnqualifiedInitializationEffects(string source)
    {
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var semantic = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.ParseFile(fixture.ScriptPath) }, "net10.0", PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Contains(semantic.Bound.Diagnostics, diagnostic => diagnostic.Code == "PSB2951");
        Assert.DoesNotContain(semantic.Bound.Functions, function => function.Symbol.Kind == PowerShellSymbolKind.ModuleInitializer);
    }
}
