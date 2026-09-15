namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;

    /// <summary>Installs compiler-selected regions while preserving the retained statements' original AST extents.</summary>
    public static class PowerShellHybridRegionHost
    {
        /// <summary>Creates a retained function using the original single-prefix runtime contract.</summary>
        public static ScriptBlock Create(PSModuleInfo module, string source, string sourcePath,
            int functionStart, int functionEnd, int[] starts, int[] ends, string[] replacements, bool[] guarded, string[] localNames)
            => Create(module, source, sourcePath, functionStart, functionEnd, starts, ends, replacements, guarded,
                Array.Empty<string>(), Array.Empty<string>(), starts == null ? Array.Empty<int>() : new int[starts.Length], localNames);

        /// <summary>Creates a module-owned retained function with immutable statement-aligned region replacements.</summary>
        /// <remarks>The replacements are compiler output. Runtime validation checks shape and source identity, not semantic eligibility.</remarks>
        public static ScriptBlock Create(PSModuleInfo module, string source, string sourcePath,
            int functionStart, int functionEnd, int[] starts, int[] ends, string[] replacements, bool[] guarded,
            string[] inputLocalNames, string[] inputLocalTypeNames, int[] inputLocalCounts, string[] localNames)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (starts == null || ends == null || replacements == null || guarded == null ||
                starts.Length == 0 || starts.Length != ends.Length || starts.Length != replacements.Length || starts.Length != guarded.Length)
                throw new ArgumentException("Every retained region requires a complete replacement contract.");
            if (inputLocalNames == null || inputLocalTypeNames == null || inputLocalCounts == null ||
                inputLocalNames.Length != inputLocalTypeNames.Length || inputLocalCounts.Length != starts.Length ||
                inputLocalCounts.Any(static count => count < 0) || inputLocalCounts.Sum() != inputLocalNames.Length ||
                inputLocalCounts[0] != 0)
                throw new ArgumentException("Every detached region requires a complete live input-local contract.", nameof(inputLocalCounts));
            if (localNames == null || localNames.Length == 0 || !guarded[0] || guarded.Skip(1).Any(value => value))
                throw new ArgumentException("The retained region host requires one guarded initialization prefix.", nameof(guarded));
            var document = PowerShellNativeFunctionHost.ParseSelectedDocument(source, sourcePath, functionStart, functionEnd);
            var function = document.FindAll(node => node is FunctionDefinitionAst &&
                node.Extent.StartOffset == functionStart && node.Extent.EndOffset == functionEnd, true)
                .Cast<FunctionDefinitionAst>().SingleOrDefault()
                ?? throw new ArgumentException("The retained function does not match its authored source identity.", nameof(source));
            var body = function.Body;
            if (function.IsFilter || function.IsWorkflow || body.DynamicParamBlock != null || body.BeginBlock != null || body.ProcessBlock != null ||
                body.EndBlock == null || !body.EndBlock.Unnamed ||
                typeof(ScriptBlockAst).GetProperty("CleanBlock")?.GetValue(body, null) != null ||
                body.EndBlock.Traps?.Count > 0)
                throw new ArgumentException("The retained region host requires an unnamed function body without traps.", nameof(source));
            var authored = body.EndBlock.Statements;
            if (authored.Count == 0 || starts[0] != authored[0].Extent.StartOffset)
                throw new ArgumentException("The ownership condition must precede every authored body statement.", nameof(starts));
            var statements = new List<StatementAst>();
            var retainedStatements = new List<StatementAst>();
            var next = 0;
            var inputLocalOffset = 0;
            for (var region = 0; region < starts.Length; region++)
            {
                while (next < authored.Count && authored[next].Extent.StartOffset < starts[region])
                {
                    retainedStatements.Add((StatementAst)authored[next].Copy());
                    statements.Add((StatementAst)authored[next++].Copy());
                }
                if (next >= authored.Count || authored[next].Extent.StartOffset != starts[region] || ends[region] <= starts[region])
                    throw new ArgumentException("The retained region does not begin at an ordered statement boundary.", nameof(starts));
                var original = new List<StatementAst>();
                while (next < authored.Count && authored[next].Extent.EndOffset <= ends[region])
                    original.Add((StatementAst)authored[next++].Copy());
                if (original.Count == 0 || original[original.Count - 1].Extent.EndOffset != ends[region])
                    throw new ArgumentException("The retained region does not end at a statement boundary.", nameof(ends));
                var replacement = PowerShellNativeFunctionHost.Parse(replacements[region], sourcePath);
                if (replacement.ParamBlock != null || replacement.BeginBlock != null || replacement.ProcessBlock != null ||
                    replacement.DynamicParamBlock != null || replacement.EndBlock?.Statements.Count != 1)
                    throw new ArgumentException("A retained region replacement requires one compiler-owned statement.", nameof(replacements));
                StatementAst statement = replacement.EndBlock.Statements[0];
                var inputCount = inputLocalCounts[region];
                if (inputCount > 0)
                {
                    var names = inputLocalNames.Skip(inputLocalOffset).Take(inputCount).ToArray();
                    var types = inputLocalTypeNames.Skip(inputLocalOffset).Take(inputCount).ToArray();
                    statement = CreateInputLocalChoice(statement, original, names, types, body.EndBlock.Extent, sourcePath);
                }
                inputLocalOffset += inputCount;
                statements.Add((StatementAst)statement.Copy());
                if (guarded[region]) retainedStatements.AddRange(original);
                else retainedStatements.Add((StatementAst)statement.Copy());
            }
            while (next < authored.Count)
            {
                retainedStatements.Add((StatementAst)authored[next].Copy());
                statements.Add((StatementAst)authored[next++].Copy());
            }
            var parameters = body.ParamBlock != null ? (ParamBlockAst)body.ParamBlock.Copy() :
                new ParamBlockAst(body.Extent, null, function.Parameters?.Select(parameter => (ParameterAst)parameter.Copy()) ?? Array.Empty<ParameterAst>());
            // Preserve the original tuple declaration order and constraints without executing
            // these declarations. The actual initialization choice is outside native statements.
            var placeholder = (IfStatementAst)PowerShellNativeFunctionHost.Parse("if ($false) { }", sourcePath).EndBlock.Statements[0];
            statements.Insert(0, new IfStatementAst(body.EndBlock.Extent,
                new[] { Tuple.Create((PipelineBaseAst)placeholder.Clauses[0].Item1.Copy(),
                    new StatementBlockAst(body.EndBlock.Extent, authored.Select(statement => (StatementAst)statement.Copy()), null)) }, null));
            var statementBlock = new StatementBlockAst(body.EndBlock.Extent, statements, null);
            var rewritten = CreateBody(document, body, parameters, statementBlock, function.IsFilter);
            var retained = new FunctionDefinitionAst(function.Extent, function.IsFilter, function.IsWorkflow,
                function.Name, null, rewritten);
            // A native function's metadata owner is FunctionDefinitionAst, not just its body.
            // Windows PowerShell also requires that owner to find comment-based function help.
            var originalScript = module.NewBoundScriptBlock(PowerShellNativeFunctionHost.CreateFunctionScriptBlock(function));
            var compiledScript = module.NewBoundScriptBlock(PowerShellNativeFunctionHost.CreateFunctionScriptBlock(retained));
            ScriptBlock? retainedScript = null;
            if (starts.Length > 1)
            {
                // Other independently approved regions must still execute when this prefix
                // keeps its native assignments. Do not silently guard unrelated suffixes.
                retainedStatements.Insert(0, (StatementAst)statements[0].Copy());
                var retainedBody = CreateBody(document, body, (ParamBlockAst)parameters.Copy(),
                    new StatementBlockAst(body.EndBlock.Extent, retainedStatements, null), function.IsFilter);
                var retainedFunction = new FunctionDefinitionAst(function.Extent, function.IsFilter, function.IsWorkflow,
                    function.Name, null, retainedBody);
                retainedScript = module.NewBoundScriptBlock(PowerShellNativeFunctionHost.CreateFunctionScriptBlock(retainedFunction));
            }
            InstallChoice(originalScript, compiledScript, retainedScript, localNames);
            return originalScript;
        }

        /// <summary>Checks a detached region's transferred locals at the region's live execution boundary.</summary>
        public static bool CanUpdateLocals(SessionState session, string[] names, string[] typeNames)
            => PowerShellRegionLocalOwnership.CanUpdateLocals(session, names, typeNames);

        private static StatementAst CreateInputLocalChoice(StatementAst replacement, IReadOnlyList<StatementAst> original,
            string[] names, string[] typeNames, IScriptExtent extent, string sourcePath)
        {
            static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
            var conditionSource = "if ([PowerForge.Generated.Runtime.PowerShellHybridRegionHost]::CanUpdateLocals(" +
                "$ExecutionContext.SessionState, [string[]]@(" + string.Join(", ", names.Select(Quote)) +
                "), [string[]]@(" + string.Join(", ", typeNames.Select(Quote)) + "))) { }";
            var condition = (IfStatementAst)PowerShellNativeFunctionHost.Parse(conditionSource, sourcePath).EndBlock.Statements[0];
            return new IfStatementAst(extent,
                new[] { Tuple.Create((PipelineBaseAst)condition.Clauses[0].Item1.Copy(),
                    new StatementBlockAst(extent, new[] { (StatementAst)replacement.Copy() }, null)) },
                new StatementBlockAst(extent, original.Select(static item => (StatementAst)item.Copy()), null));
        }

        private static void InstallChoice(ScriptBlock original, ScriptBlock compiled, ScriptBlock? retained, string[] localNames)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var contract = PowerShellNativeFunctionHost.NativeContract.Shared;
            foreach (var script in new[] { original, compiled, retained })
            {
                if (script == null) continue;
                PowerShellNativeFunctionHost.Invoke(contract.Compile, script, new object[] { false });
                PowerShellNativeFunctionHost.Invoke(contract.Compile, script, new object[] { true });
            }
            var originalData = contract.Data.GetValue(original)!;
            var compiledData = contract.Data.GetValue(compiled)!;
            var retainedData = retained == null ? null : contract.Data.GetValue(retained)!;
            var dataType = originalData.GetType();
            var names = dataType.GetProperty("NameToIndexMap", flags)!;
            var originalNames = (Dictionary<string, int>)names.GetValue(originalData, null)!;
            foreach (var alternativeData in new[] { compiledData, retainedData })
            {
                if (alternativeData == null) continue;
                var alternativeNames = (Dictionary<string, int>)names.GetValue(alternativeData, null)!;
                if (originalNames.Count != alternativeNames.Count || originalNames.Any(pair =>
                        !alternativeNames.TryGetValue(pair.Key, out var index) || pair.Value != index))
                    throw new NotSupportedException("The promoted prefix does not preserve the invocation's native local slots.");
            }
            foreach (var optimized in new[] { false, true })
            {
                var tuple = dataType.GetProperty(optimized ? "LocalsMutableTupleType" : "UnoptimizedLocalsMutableTupleType", flags)!;
                if (!Equals(tuple.GetValue(originalData, null), tuple.GetValue(compiledData, null)) ||
                    retainedData != null && !Equals(tuple.GetValue(originalData, null), tuple.GetValue(retainedData, null)))
                    throw new NotSupportedException("The promoted prefix does not preserve the invocation's native tuple type.");
                var clause = dataType.GetProperty(optimized ? "EndBlock" : "UnoptimizedEndBlock", flags)!;
                var native = clause.GetValue(originalData, null)!;
                var context = Expression.Parameter(contract.FunctionContextType, "context");
                Expression InvokeAlternative(object alternativeData)
                {
                    var points = dataType.GetProperty("SequencePoints", flags)!.GetValue(alternativeData, null)!;
                    var alternative = clause.GetValue(alternativeData, null)!;
                    var previousPoints = Expression.Variable(contract.SequencePoints.FieldType, "previousPoints");
                    var previousIndex = Expression.Variable(typeof(int), "previousIndex");
                    return Expression.Block(new[] { previousPoints, previousIndex },
                        Expression.Assign(previousPoints, Expression.Field(context, contract.SequencePoints)),
                        Expression.Assign(previousIndex, Expression.Field(context, contract.SequenceIndex)),
                        Expression.TryFinally(Expression.Block(
                            Expression.Assign(Expression.Field(context, contract.SequencePoints), Expression.Constant(points, contract.SequencePoints.FieldType)),
                            Expression.Assign(Expression.Field(context, contract.SequenceIndex), Expression.Constant(0)),
                            Expression.Invoke(Expression.Constant(alternative, clause.PropertyType), context)),
                            Expression.Block(
                                Expression.Assign(Expression.Field(context, contract.SequencePoints), previousPoints),
                                Expression.Assign(Expression.Field(context, contract.SequenceIndex), previousIndex))));
                }
                var condition = Expression.Call(typeof(PowerShellHybridRegionHost).GetMethod(nameof(CanInitialize), BindingFlags.Static | BindingFlags.NonPublic)!,
                    Expression.Convert(context, typeof(object)), Expression.Constant(optimized), Expression.Constant(localNames));
                var choice = Expression.Lambda(clause.PropertyType, Expression.IfThenElse(condition, InvokeAlternative(compiledData),
                    retainedData == null ? Expression.Invoke(Expression.Constant(native, clause.PropertyType), context) : InvokeAlternative(retainedData)), context).Compile();
                clause.SetValue(originalData, choice, null);
            }
        }

        private static bool CanInitialize(object nativeContext, bool optimized, string[] localNames)
        {
            using (var context = new PowerShellNativeFunctionContext(nativeContext, optimized))
                return PowerShellRegionLocalOwnership.CanInitializeLocals(context.SessionState, localNames);
        }

        private static ScriptBlockAst CreateBody(ScriptBlockAst document, ScriptBlockAst body,
            ParamBlockAst parameters, StatementBlockAst statements, bool isFilter)
        {
            // The installed 5.1 engine has using metadata omitted by its reference assembly.
            var usingStatements = typeof(ScriptBlockAst).GetProperty("UsingStatements")?.GetValue(document, null) as System.Collections.IEnumerable;
            var selected = new List<Ast>();
            if (usingStatements != null)
                foreach (Ast statement in usingStatements)
                {
                    // UsingStatementAst.Copy reuses its already-parented namespace name on
                    // supported engines. Reconstruct the declaration with a copied name.
                    var statementType = statement.GetType();
                    var kind = statementType.GetProperty("UsingStatementKind")!.GetValue(statement, null)!;
                    if (!string.Equals(kind.ToString(), "Namespace", StringComparison.Ordinal))
                        throw new NotSupportedException("Detached function metadata supports namespace imports only.");
                    var name = (Ast)statementType.GetProperty("Name")!.GetValue(statement, null)!;
                    selected.Add((Ast)Activator.CreateInstance(statementType, new object[] { statement.Extent, kind, name.Copy() })!);
                }
            if (selected.Count == 0) return new ScriptBlockAst(body.Extent, parameters, statements, isFilter);
            var constructor = typeof(ScriptBlockAst).GetConstructors().Single(candidate =>
            {
                var arguments = candidate.GetParameters();
                return arguments.Length == 6 && arguments[1].ParameterType.IsGenericType &&
                    arguments[1].ParameterType.GetGenericArguments()[0].Name == "UsingStatementAst" &&
                    arguments[2].ParameterType == typeof(ParamBlockAst) && arguments[3].ParameterType == typeof(StatementBlockAst);
            });
            var elementType = constructor.GetParameters()[1].ParameterType.GetGenericArguments()[0];
            var array = Array.CreateInstance(elementType, selected.Count);
            for (var index = 0; index < selected.Count; index++) array.SetValue(selected[index], index);
            return (ScriptBlockAst)constructor.Invoke(new object[] { body.Extent, array, parameters, statements, isFilter, false });
        }
    }
}
