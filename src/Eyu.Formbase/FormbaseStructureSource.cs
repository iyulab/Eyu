using Eyu.Core.Declared;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Formbase.Core.Ports;
using Formbase.Core.Primitives;

namespace Eyu.Formbase;

/// <summary>
/// Adapts a Formbase <see cref="IFieldHintSource"/> to Eyu's <see cref="IStructureSource"/>.
/// Declared field hints carry through unchanged in meaning — Formbase's declaration is the
/// answer, Eyu never re-infers what Formbase already declared (design rationale §A). Eyu.Core
/// does not reference Formbase.Core; this adapter is the one place that bridges the two, keeping
/// Eyu's source-agnostic promise intact for every other consumer.
/// </summary>
public sealed class FormbaseStructureSource(IFieldHintSource hintSource) : IStructureSource
{
    public async Task<DeclaredStructure?> GetStructureAsync(SubjectRef subject, CancellationToken cancellationToken = default)
    {
        var type = FormTypeRef.Create(subject.Value);
        var hints = await hintSource.GetHintsAsync(type, cancellationToken).ConfigureAwait(false);
        if (hints is null)
        {
            return null;
        }

        var fields = hints.Fields
            .Select(field => new DeclaredField(field.Name))
            .ToList();

        var relations = (hints.Relations ?? [])
            .Select(relation => new DeclaredRelation(relation.Name, SubjectRef.Create(relation.Target.Value)))
            .ToList();

        return new DeclaredStructure(subject, fields, relations, hints.DeclarationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
