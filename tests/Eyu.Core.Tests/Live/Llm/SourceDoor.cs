using System.Text.RegularExpressions;
using Eyu.Core.Records;
using Eyu.Core.Tests.Quality;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>The ways the quality measurement can hand records to the model, chosen by <see cref="SourceDoor.Variable"/>.</summary>
public enum SourceDoorKind
{
    /// <summary>The catalog's records as they are.</summary>
    Identity,

    /// <summary>The catalog's <c>yyyy-MM-dd</c> values written as <c>dd/MM/yyyy</c>.</summary>
    DateSlash,

    /// <summary>The catalog's <c>yyyy-MM-dd</c> values written as <c>yyyyMMdd</c>.</summary>
    DateCompact,

    /// <summary>The rows of the door cases as a table export carries them (<see cref="DoorCase.ThroughTable"/>).</summary>
    Table,

    /// <summary>The rows of the door cases through the Formbase sampler, a blank cell left out of the document.</summary>
    Formbase,

    /// <summary>The rows of the door cases through the Formbase sampler, a blank cell filed as JSON <c>null</c>.</summary>
    FormbaseNull,

    /// <summary>
    /// The rows of the door cases as a table export carries them, with the case's natural key column given as the
    /// record key — the table door with the one thing the Formbase door adds that is information about identity.
    /// </summary>
    TableKeyed,

    /// <summary>
    /// The rows of the door cases through the Formbase sampler, a blank cell left out, with no record key filed — the
    /// Formbase door without the one thing the table door lacks that is information about identity.
    /// </summary>
    FormbaseNoKey,
}

/// <summary>
/// Reads which door a measurement round hands its records through. The first three kinds rewrite the catalog's
/// own records (a spelling of a date, nothing else); the rest are the real doors, which measure the door
/// cases of <see cref="DoorCase.FolderVariable"/> instead and leave the catalog out — a round that measured both
/// would spend its calls on cases with no door to compare.
/// </summary>
internal static class SourceDoor
{
    public const string Variable = "EYU_LLM_QUALITY_DOOR";

    public static SourceDoorKind Parse(string? value) => value switch
    {
        null or "" or "identity" => SourceDoorKind.Identity,
        "date-slash" => SourceDoorKind.DateSlash,
        "date-compact" => SourceDoorKind.DateCompact,
        "table" => SourceDoorKind.Table,
        "formbase" => SourceDoorKind.Formbase,
        "formbase-null" => SourceDoorKind.FormbaseNull,
        "table-keyed" => SourceDoorKind.TableKeyed,
        "formbase-nokey" => SourceDoorKind.FormbaseNoKey,
        _ => throw new ArgumentException($"{Variable} '{value}' is not identity, date-slash, date-compact, table, formbase, formbase-null, table-keyed or formbase-nokey."),
    };

    /// <summary>Whether the round measures the door cases through a real door rather than the catalog.</summary>
    public static bool ReadsDoorCases(SourceDoorKind kind) => kind is SourceDoorKind.Table or SourceDoorKind.Formbase or SourceDoorKind.FormbaseNull or SourceDoorKind.TableKeyed or SourceDoorKind.FormbaseNoKey;

    /// <summary>
    /// The case's name in the report: the case and the door it went through, so the two doors' proposals of one
    /// case sit side by side and a Turtle file is named by both.
    /// </summary>
    public static string CaseName(string caseName, SourceDoorKind kind) => $"{caseName}@{Label(kind)}";

    /// <summary>The records of <paramref name="doorCase"/> as <paramref name="kind"/> hands them over.</summary>
    public static Task<RawRecord[]> BuildAsync(DoorCase doorCase, SourceDoorKind kind, CancellationToken cancellationToken) => kind switch
    {
        SourceDoorKind.Table => Task.FromResult(doorCase.ThroughTable()),
        SourceDoorKind.Formbase => FormbaseDoor.ThroughAsync(doorCase, BlankCell.Omitted, cancellationToken),
        SourceDoorKind.FormbaseNull => FormbaseDoor.ThroughAsync(doorCase, BlankCell.Null, cancellationToken),
        SourceDoorKind.TableKeyed => Task.FromResult(doorCase.ThroughTable(withKey: true)),
        SourceDoorKind.FormbaseNoKey => FormbaseDoor.ThroughAsync(doorCase, BlankCell.Omitted, cancellationToken, withKey: false),
        _ => throw new ArgumentException($"{Variable} '{Label(kind)}' is not a door the door cases are read through (table, formbase, formbase-null, table-keyed, formbase-nokey)."),
    };

    /// <summary>
    /// The catalog's records as the kind writes them: the date rewrites change a value's spelling and nothing else about the
    /// case; the others hand the records over as they are.
    /// </summary>
    public static RawRecord[] RewriteCatalog(RawRecord[] records, SourceDoorKind kind)
    {
        Func<string, string>? rewrite = kind switch
        {
            SourceDoorKind.DateSlash => value => Regex.Replace(value, @"^(\d{4})-(\d{2})-(\d{2})$", "$3/$2/$1"),
            SourceDoorKind.DateCompact => value => Regex.Replace(value, @"^(\d{4})-(\d{2})-(\d{2})$", "$1$2$3"),
            _ => null,
        };
        return rewrite is null
            ? records
            : [.. records.Select(r => new RawRecord(r.Id, r.Fields.ToDictionary(f => f.Key, f => f.Value is null ? null : rewrite(f.Value)), r.Subject, r.Key))];
    }

    private static string Label(SourceDoorKind kind) => kind switch
    {
        SourceDoorKind.Identity => "identity",
        SourceDoorKind.DateSlash => "date-slash",
        SourceDoorKind.DateCompact => "date-compact",
        SourceDoorKind.Table => "table",
        SourceDoorKind.Formbase => "formbase",
        SourceDoorKind.FormbaseNull => "formbase-null",
        SourceDoorKind.TableKeyed => "table-keyed",
        SourceDoorKind.FormbaseNoKey => "formbase-nokey",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
