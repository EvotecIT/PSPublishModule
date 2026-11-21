namespace PowerForge.Generated.Runtime;

internal sealed class PowerShellStrictPipelineStage<T>
{
    private bool _started;
    private Action _begin = null!;
    private Action<T> _process = null!;
    private Action _end = null!;
    private Action _clean = null!;

    internal Action<object?> Output { get; set; } = null!;

    internal void Configure(Action begin, Action<T> process, Action end, Action clean)
    {
        _begin = begin;
        _process = process;
        _end = end;
        _clean = clean;
    }

    internal void Start()
    {
        if (_started) return;
        _started = true;
        _begin();
    }

    internal void Process(T record)
    {
        Start();
        _process(record);
    }

    internal void End() => _end();

    internal void Clean()
    {
        if (_started) _clean();
    }
}

internal static class PowerShellStrictPipelineComposition
{
    internal static void Run<TInput, TIntermediate>(
        TInput[]? input, bool processNull, TInput nullRecord, TIntermediate nullIntermediate,
        PowerShellStrictPipelineStage<TInput> upstream,
        PowerShellStrictPipelineStage<TIntermediate> downstream,
        int first, Action<object?> output)
    {
        var stop = new object();
        upstream.Output = record => downstream.Process(record is null ? nullIntermediate : (TIntermediate)record);
        downstream.Output = SelectOutput(first, output, stop);
        try
        {
            upstream.Start();
            downstream.Start();
            ProcessInput(input, processNull, nullRecord, upstream);
            upstream.End();
            downstream.End();
        }
        catch (PowerShellStrictPipelineStopSignal signal) when (ReferenceEquals(signal.Owner, stop)) { }
        finally
        {
            try { upstream.Clean(); }
            finally { downstream.Clean(); }
        }
    }

    internal static void RunSingle<T>(T[]? input, bool processNull, T nullRecord,
        PowerShellStrictPipelineStage<T> stage, int first, Action<object?> output)
    {
        var stop = new object();
        stage.Output = SelectOutput(first, output, stop);
        try
        {
            stage.Start();
            ProcessInput(input, processNull, nullRecord, stage);
            stage.End();
        }
        catch (PowerShellStrictPipelineStopSignal signal) when (ReferenceEquals(signal.Owner, stop)) { }
        finally { stage.Clean(); }
    }

    private static void ProcessInput<T>(T[]? input, bool processNull, T nullRecord, PowerShellStrictPipelineStage<T> stage)
    {
        if (input is null)
        {
            if (processNull) stage.Process(nullRecord);
            return;
        }
        foreach (var record in input)
            stage.Process(record);
    }

    private static Action<object?> SelectOutput(int first, Action<object?> output, object stop)
        => first == 0 ? output : record =>
        {
            if (first == 0) throw new PowerShellStrictPipelineStopSignal(stop);
            if (record is null) return;
            output(record);
            if (--first == 0) throw new PowerShellStrictPipelineStopSignal(stop);
        };
}

internal sealed class PowerShellStrictPipelineStopSignal : Exception
{
    internal PowerShellStrictPipelineStopSignal(object owner) => Owner = owner;

    internal object Owner { get; }
}
