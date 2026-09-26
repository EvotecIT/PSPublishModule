namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static void ValidateSelectedSyntax(ParseError[] documentErrors, string fragment, string parameterName)
        {
            if (documentErrors.Length == 0) return;
            // A source document can contain another command's newer syntax. Only a matched,
            // independently valid fragment reaches compilation; unrelated recovered ASTs do not.
            Parser.ParseInput(fragment, out _, out var fragmentErrors);
            if (fragmentErrors.Length != 0)
                throw new ArgumentException(fragmentErrors[0].Message, parameterName);
        }

        // Builds narrowly selected native AST operations against the invocation's existing
        // execution context and local tuple. It never creates another scope or runs a body.
        private sealed class NativeAstCompiler
        {
            private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            internal Type CompilerType { get; }
            internal object Compiler { get; }
            internal ParameterExpression Context { get; }
            internal ParameterExpression RightHandSide { get; } = Expression.Parameter(typeof(Func<object?>), "rightHandSide");
            internal List<Expression> Expressions { get; }
            internal List<ParameterExpression> Temporaries { get; }
            private readonly IScriptExtent _fallbackExtent;
            private readonly bool _preserveSelectedSequencePoints;

            internal NativeAstCompiler(PowerShellNativeFunctionContext owner, ScriptBlockAst ast, bool assignmentVariables = false,
                bool preserveSelectedSequencePoints = false)
            {
                _preserveSelectedSequencePoints = preserveSelectedSequencePoints;
                _fallbackExtent = ast.EndBlock.Statements[0].Extent;
                var assembly = typeof(PSObject).Assembly;
                CompilerType = assembly.GetType("System.Management.Automation.Language.Compiler", true)!;
                var analysisType = assembly.GetType("System.Management.Automation.Language.VariableAnalysis", true)!;
                var functionScript = (ScriptBlock)owner.FunctionContext.GetType().GetField("_scriptBlock", Instance)!
                    .GetValue(owner.FunctionContext)!;
                var usesCmdletBinding = (bool)typeof(ScriptBlock).GetProperty("UsesCmdletBinding", Instance)!
                    .GetValue(functionScript, null)!;
                var analyze = analysisType.GetMethods(Static).Single(method => method.Name == "Analyze" && method.GetParameters().Length == 3);
                PowerShellNativeFunctionHost.Invoke(analyze, null, new object[] { ast, true, usesCmdletBinding });
                Compiler = Activator.CreateInstance(CompilerType, nonPublic: true)!;
                CompilerType.GetProperty("Optimize", Instance)!.SetValue(Compiler, assignmentVariables && owner._optimized, null);
                CompilerType.GetField("_compilingScriptCmdlet", Instance)!.SetValue(Compiler, usesCmdletBinding);
                CompilerType.GetField("_switchTupleIndex", Instance)!.SetValue(Compiler, -2);
                CompilerType.GetField("_foreachTupleIndex", Instance)!.SetValue(Compiler, -2);

                // Analysis keeps ordinary variables dynamic. Automatic slots are emitted against
                // this invocation's actual tuple, rather than allocating a region-local scope.
                var tupleType = owner._contract.LocalsTuple.GetValue(owner.FunctionContext)!.GetType();
                if (assignmentVariables && owner._optimized)
                {
                    var tuple = owner._contract.LocalsTuple.GetValue(owner.FunctionContext)!;
                    var tupleBase = assembly.GetType("System.Management.Automation.MutableTuple", true)!;
                    var names = (Dictionary<string, int>)tupleBase.GetField("_nameToIndexMap", Instance)!.GetValue(tuple)!;
                    var tupleIndex = typeof(VariableExpressionAst).GetProperty("TupleIndex", Instance)!;
                    foreach (var variable in ast.FindAll(node => node is VariableExpressionAst, searchNestedScriptBlocks: false).Cast<VariableExpressionAst>())
                    {
                        var path = variable.VariablePath;
                        var name = path.IsLocal ? path.UserPath.Substring(6) : path.UserPath;
                        if ((path.IsUnqualified || path.IsLocal) && names.TryGetValue(name, out var index))
                            tupleIndex.SetValue(variable, index, null);
                    }
                }
                var locals = Expression.Variable(tupleType, "locals");
                CompilerType.GetProperty("LocalVariablesTupleType", Instance)!.SetValue(Compiler, tupleType, null);
                CompilerType.GetProperty("LocalVariablesParameter", Instance)!.SetValue(Compiler, locals, null);
                Context = (ParameterExpression)(CompilerType.GetField("s_functionContext", Static) ??
                    CompilerType.GetField("_functionContext", Static) ??
                    throw new NotSupportedException("PowerShell's native compiler context parameter is unavailable.")).GetValue(null)!;
                var execution = (ParameterExpression)(CompilerType.GetField("s_executionContextParameter", Static) ??
                    CompilerType.GetField("_executionContextParameter", Static) ??
                    throw new NotSupportedException("PowerShell's native compiler execution parameter is unavailable.")).GetValue(null)!;
                Expressions = new List<Expression>
                {
                    Expression.Assign(execution, Expression.Field(Context, owner._contract.ExecutionContext)),
                    Expression.Assign(locals, Expression.Convert(Expression.Field(Context, owner._contract.LocalsTuple), tupleType))
                };
                Temporaries = new List<ParameterExpression> { execution, locals };
            }

            internal object? Invoke(string method, params object[] arguments)
                => PowerShellNativeFunctionHost.Invoke(CompilerType.GetMethod(method, Instance)!, Compiler, arguments);

            internal NativeAstOperation Compile()
            {
                var lambda = Expression.Lambda(Expression.Block(typeof(object), Temporaries, Expressions), Context, RightHandSide);
                var context = Expression.Parameter(typeof(object), "context");
                var body = Expression.Lambda<Func<object, Func<object?>?, object?>>(
                    Expression.Invoke(lambda, Expression.Convert(context, Context.Type), RightHandSide), context, RightHandSide).Compile();
                var points = ((IEnumerable<IScriptExtent>)CompilerType.GetField("_sequencePoints", Instance)!.GetValue(Compiler)!).ToArray();
                var hasCompiledSequencePoints = _preserveSelectedSequencePoints && points.Length != 0;
                if (points.Length == 0) points = new[] { _fallbackExtent };
                return new NativeAstOperation(body, points, Context.Type, hasCompiledSequencePoints);
            }
        }

        private sealed class NativeAstOperation
        {
            private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private readonly Func<object, Func<object?>?, object?> _body;
            private readonly IScriptExtent[] _points;
            private readonly bool _hasCompiledSequencePoints;
            private readonly FieldInfo _sequencePoints, _sequenceIndex;

            internal NativeAstOperation(Func<object, Func<object?>?, object?> body, IScriptExtent[] points, Type contextType,
                bool hasCompiledSequencePoints)
            {
                _body = body;
                _points = points;
                _hasCompiledSequencePoints = hasCompiledSequencePoints;
                _sequencePoints = contextType.GetField("_sequencePoints", Instance)!;
                _sequenceIndex = contextType.GetField("_currentSequencePointIndex", Instance)!;
            }

            internal object? Invoke(object context, Func<object?>? rightHandSide = null, bool rightHandSideFirst = false)
            {
                var previousPoints = _sequencePoints.GetValue(context);
                var previousIndex = _sequenceIndex.GetValue(context);
                var preTargetFailure = false;
                if (_hasCompiledSequencePoints && rightHandSideFirst && rightHandSide != null)
                {
                    var evaluateRightHandSide = rightHandSide;
                    rightHandSide = () =>
                    {
                        try { return evaluateRightHandSide(); }
                        catch { preTargetFailure = true; throw; }
                    };
                }
                try
                {
                    _sequencePoints.SetValue(context, _points);
                    _sequenceIndex.SetValue(context, 0);
                    return _body(context, rightHandSide);
                }
                catch (Exception error) when (PowerShellStatementErrorContext.IsOperationFailure(error))
                {
                    var index = (int)_sequenceIndex.GetValue(context)!;
                    // A simple assignment's RHS precedes the target. Compound assignment
                    // has already visited its key, so its point remains active on RHS failure.
                    if (_hasCompiledSequencePoints && !preTargetFailure && index >= 0 && index < _points.Length)
                        PowerShellStatementErrorContext.RememberNativeExpressionFailure(error, _points[index]);
                    throw;
                }
                finally
                {
                    _sequenceIndex.SetValue(context, previousIndex);
                    _sequencePoints.SetValue(context, previousPoints);
                }
            }
        }
    }
}
