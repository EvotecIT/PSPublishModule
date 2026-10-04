using System;

// An explicit test boundary with no imports, sockets, directory providers or native calls.
public static class PfcOwnedPasswordCall
{
    public static int Calls;
    public static bool FailureResult;
    public static bool ThrowOnCall;
    public static bool ArgumentsMatched;

    public static bool NetUserChangePassword(string domain, string username, string oldpassword, string newpassword)
    {
        Calls++;
        ArgumentsMatched = domain == "owned-target.invalid" && username == "owned-user"
            && oldpassword == "owned-old-value" && newpassword == "owned-new-value";
        if (!ArgumentsMatched) throw new InvalidOperationException("Owned argument mismatch");
        if (ThrowOnCall) throw new InvalidOperationException("Owned native boundary refusal");
        return FailureResult;
    }
}
