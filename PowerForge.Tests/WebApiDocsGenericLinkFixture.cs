namespace PowerForge.Tests.GenericLinks
{
    public class Prompt { }
    public class Prompt<T> : Prompt { }
    public class Dictionary<TKey, TValue> { }
    public class ConfirmPrompt : Prompt<int> { }
    public class Factory { public Fallback<int> Create() => new(); }
    public class Fallback<TValue> { }
    public class Outer<TOuter> { public class Inner<TInner> { } }
}

namespace PowerForge.Tests.OtherGenericLinks
{
    public class Prompt { }
    public class Prompt<TValue> { }
}
