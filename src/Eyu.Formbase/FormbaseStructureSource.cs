using Eyu.Core.Declared;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Formbase.Core.Ports;
using Formbase.Core.Primitives;
using Formbase.Core.Schema;

namespace Eyu.Formbase;

/// <summary>
/// Adapts a Formbase <see cref="IFieldHintSource"/> to Eyu's <see cref="IStructureSource"/>.
/// Declared field hints carry through unchanged in meaning — the name, the value type, whether the
/// field is required, and for a relation which field carries its key and on which side — because
/// Formbase's declaration is the answer and Eyu never re-infers what Formbase already declared
/// (design rationale §A). Formbase's own type names stop here: they are mapped onto Eyu's declared
/// vocabulary rather than passed through, so Eyu.Core never references Formbase.Core and this
/// adapter stays the one place that bridges the two.
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
            .Select(field => new DeclaredField(
                field.Name,
                Kind: ToDeclaredKind(field.Type),
                Required: !field.Nullable))
            .ToList();

        var relations = (hints.Relations ?? [])
            .Select(relation => new DeclaredRelation(
                relation.Name,
                SubjectRef.Create(relation.Target.Value),
                ViaField: relation.KeyField,
                Kind: ToDeclaredKind(relation.Kind)))
            .ToList();

        return new DeclaredStructure(subject, fields, relations, hints.DeclarationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static DeclaredValueKind ToDeclaredKind(ColumnType type) => type switch
    {
        ColumnType.Text => DeclaredValueKind.Text,
        ColumnType.Integer => DeclaredValueKind.WholeNumber,
        ColumnType.Decimal => DeclaredValueKind.FractionalNumber,
        ColumnType.Boolean => DeclaredValueKind.Boolean,
        ColumnType.Timestamp => DeclaredValueKind.Timestamp,
        ColumnType.Uuid => DeclaredValueKind.Identifier,
        ColumnType.Jsonb => DeclaredValueKind.Structured,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Formbase declared a column type this adapter does not know how to carry."),
    };

    private static DeclaredRelationKind ToDeclaredKind(RelationKind kind) => kind switch
    {
        RelationKind.Reference => DeclaredRelationKind.Reference,
        RelationKind.Child => DeclaredRelationKind.Child,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Formbase declared a relation kind this adapter does not know how to carry."),
    };
}
