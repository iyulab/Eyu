namespace Eyu.Core.Proposals;

/// <summary>
/// A type name the model's answer misspelled, written in the proposal as the name already in use: the
/// two were a letter or two apart, only one name in use was that near, and the records the answer cited
/// carry the name in use's words and not the answered ones. Nearness alone never respells a name — the
/// cited records decide it — so a near name they do not settle is left as answered and does not appear here.
/// </summary>
/// <param name="Answered">The type name as the answer wrote it.</param>
/// <param name="InUse">The name in use it was written as — a declared business name's type, a known type, or a known entity's type.</param>
/// <param name="Sources">The records whose words settled it (the cited sources of the element that carried the name).</param>
public sealed record RespelledTypeName(string Answered, string InUse, IReadOnlyList<string> Sources);
