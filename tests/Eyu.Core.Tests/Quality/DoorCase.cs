using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eyu.Core.Primitives;
using Eyu.Core.Records;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// One real public table, cut to a few rows, that the measurement hands to the model through more than one
/// source door: the same rows, written as the records each door would produce. A case is a file in the folder
/// <c>EYU_LLM_QUALITY_DOOR_CASES</c> names — the data is a third party's and lives outside the repository, a
/// run without the variable measures the catalog alone.
/// <para>
/// The file is a JSON object: <c>name</c>, <c>subject</c> (what the rows are rows of — both doors hand it over
/// unchanged, since a subject only one door carried would be an effect of the subject and not of the door),
/// <c>columns</c> (in order), <c>key_column</c> (the natural key, or absent) and <c>rows</c> — an array of
/// objects holding every column as text, a blank cell as <c>""</c>. Anything else in the file is ignored.
/// </para>
/// <para>
/// This type is the table door: a spreadsheet or CSV export carries every cell as text, a blank one as the
/// empty string, no record key, and nothing but the position of a row to call it by. The Formbase door builds
/// the same rows through the adapter's own code and sits with the live measurements
/// (<c>FormbaseDoor</c>), which is where Formbase is referenced.
/// </para>
/// </summary>
internal sealed record DoorCase(
    string Name,
    string Subject,
    string? KeyColumn,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows)
{
    /// <summary>The environment variable naming the folder the case files are read from.</summary>
    public const string FolderVariable = "EYU_LLM_QUALITY_DOOR_CASES";

    /// <summary>The generator's index of the folder, not a case.</summary>
    private const string Manifest = "manifest.json";

    /// <summary>The cases of the folder <see cref="FolderVariable"/> names; none when it is unset.</summary>
    public static IReadOnlyList<DoorCase> FromEnvironment() =>
        Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } folder ? LoadFolder(folder) : [];

    /// <summary>Every case file of <paramref name="folder"/> in file-name order, so two runs read them in the same order.</summary>
    public static IReadOnlyList<DoorCase> LoadFolder(string folder) =>
    [
        .. Directory.EnumerateFiles(folder, "*.json")
            .Where(path => !string.Equals(Path.GetFileName(path), Manifest, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => Parse(File.ReadAllText(path), path)),
    ];

    /// <summary>
    /// Reads one case file. A file that does not hold what the measurement relies on is refused with the
    /// file named, rather than measured on whatever it did hold: a row missing a column would be read as a
    /// blank cell by one door and as a missing field by the other, and that is the very effect being measured.
    /// </summary>
    public static DoorCase Parse(string json, string source = "case")
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{source}: a door case is a JSON object.");
        }

        var name = Text(root, "name", source);
        var subject = Text(root, "subject", source);
        var columns = ReadColumns(root, source);
        string? keyColumn = null;
        if (root.TryGetProperty("key_column", out var key) && key.ValueKind != JsonValueKind.Null)
        {
            keyColumn = key.ValueKind == JsonValueKind.String ? key.GetString() : null;
            if (string.IsNullOrEmpty(keyColumn) || !columns.Contains(keyColumn, StringComparer.Ordinal))
            {
                throw new FormatException($"{source}: key_column must name one of the columns.");
            }
        }

        if (!root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
        {
            throw new FormatException($"{source}: rows must be a non-empty array.");
        }

        var parsed = new List<IReadOnlyDictionary<string, string>>();
        foreach (var row in rows.EnumerateArray())
        {
            parsed.Add(Row(row, columns, source, parsed.Count));
        }

        return new DoorCase(name, subject, keyColumn, columns, parsed);
    }

    /// <summary>The table door: every column of every row as text, a blank cell the empty string, no record key.</summary>
    public RawRecord[] ThroughTable()
    {
        var subject = SubjectRef.Create(Subject);
        return
        [
            .. Rows.Select((row, index) => new RawRecord(
                RowId(index),
                Columns.ToDictionary(column => column, column => (string?)row[column], StringComparer.Ordinal),
                subject)),
        ];
    }

    /// <summary>What a table has to call a row by: its position.</summary>
    public static string RowId(int index) => $"row-{index + 1}";

    /// <summary>
    /// A stable identity for row <paramref name="index"/> of this case, for a door that mints one. A fresh
    /// random one would change the prompt on every build, and a recorded round can be replayed only against
    /// prompts that did not change.
    /// </summary>
    public Guid RowGuid(int index) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{Name}/{index}")).AsSpan(0, 16));

    /// <summary>Whether a cell holds nothing — what the doors must agree is a blank, however each writes it.</summary>
    public static bool IsBlank(string value) => value.Length == 0;

    private static string Text(JsonElement root, string property, string source) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!
            : throw new FormatException($"{source}: {property} must be a non-empty string.");

    private static string[] ReadColumns(JsonElement root, string source)
    {
        if (!root.TryGetProperty("columns", out var element) || element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
        {
            throw new FormatException($"{source}: columns must be a non-empty array.");
        }

        var columns = element.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() : null).ToArray();
        if (columns.Any(string.IsNullOrEmpty) || columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
        {
            throw new FormatException($"{source}: columns must be distinct non-empty strings.");
        }

        return columns!;
    }

    private static Dictionary<string, string> Row(JsonElement row, string[] columns, string source, int index)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{source}: row {index + 1} is not an object.");
        }

        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (!row.TryGetProperty(column, out var cell) || cell.ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"{source}: row {index + 1} has no text cell for column '{column}'.");
            }

            cells[column] = cell.GetString()!;
        }

        return cells;
    }
}
