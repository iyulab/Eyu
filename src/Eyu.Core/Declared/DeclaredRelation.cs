using Eyu.Core.Primitives;

namespace Eyu.Core.Declared;

/// <summary>A caller-declared link from one subject to another.</summary>
public sealed record DeclaredRelation(string Name, SubjectRef Target);
