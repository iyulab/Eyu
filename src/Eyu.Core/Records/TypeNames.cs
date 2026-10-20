namespace Eyu.Core.Records;

/// <summary>
/// How a type name in use is recognized in a model's answer. A model that is asked for <c>work_order</c>
/// writes <c>WorkOrder</c> or <c>work order</c> about as often: the spelling varies, the name does not.
/// Two names are the same when they agree letter for letter once case and every character that is not a
/// letter or a digit are set aside — a rule about spelling only, never about meaning (<c>part</c> and
/// <c>item</c> stay two names).
/// </summary>
public static class TypeNames
{
    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are spellings of one type name.</summary>
    public static bool SameType(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return string.Equals(Key(a), Key(b), StringComparison.Ordinal);
    }

    internal static string Key(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));
}
