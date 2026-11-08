using System.Globalization;
using System.Text;
using Eyu.Core.Declared;
using Eyu.Core.Judgment;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// Does handing the model a record's key help it follow a reference held by that key? The same records,
/// supplier registers whose rows carry a record key and inspections that name their supplier by it, are
/// proposed twice in turn: with the key on the records (<c>keyed</c>) and without (<c>unkeyed</c>, the key
/// null, so the prompt is worded as it was before keys existed). Per attempt it counts, for each inspection,
/// whether the answer relates an entity the inspection backs to the right supplier's entity, to the wrong
/// one, or to none; and whether a supplier entity ended up denoted by an inspection record (the reference
/// read as identity), after the deterministic demotion and before it.
/// <para>
/// <c>EYU_RECKEY_RUNS</c> — attempts per arm, default 4. <c>EYU_RECKEY_REPORT</c> — a file the table is also
/// written to. The endpoint comes from the same <c>EYU_LLM_*</c> variables as the rest of the live suite.
/// </para>
/// </summary>
public class RecordKeyReferenceMeasurement(ITestOutputHelper output)
{
    private static readonly SubjectRef SupplierSubject = SubjectRef.Create("supplier");
    private static readonly SubjectRef InspectionSubject = SubjectRef.Create("inspection");

    private static readonly DeclaredStructure[] Declared =
    [
        new(SupplierSubject, [new DeclaredField("name", "the supplier's name", DeclaredValueKind.Text)], []),
        new(InspectionSubject,
            [new DeclaredField("supplierId", "the record key of the supplier inspected", DeclaredValueKind.Identifier)],
            [new DeclaredRelation("supplied_by", SupplierSubject, ViaField: "supplierId", DeclaredRelationKind.Reference)]),
    ];

    // Each supplier row is the latest version of a record that was corrected once: its Id is the version's
    // document id, its Key the supplier code the inspections hold. The codes are not in the order the rows
    // are, so without the key the data does not say which supplier an inspection means -- a model that
    // links correctly then is guessing, and an arm that does better with the key is reading it.
    private static RawRecord[] Records(bool keyed) =>
    [
        new("doc-sup-1-v2", new Dictionary<string, string?> { ["name"] = "Acme Ltd", ["city"] = "Busan" }, SupplierSubject, keyed ? "S-7" : null),
        new("doc-sup-2-v1", new Dictionary<string, string?> { ["name"] = "Hanbit Parts", ["city"] = "Daegu" }, SupplierSubject, keyed ? "S-3" : null),
        new("doc-ins-1", new Dictionary<string, string?> { ["lot"] = "L-100", ["supplierId"] = "S-7", ["result"] = "pass" }, InspectionSubject),
        new("doc-ins-2", new Dictionary<string, string?> { ["lot"] = "L-101", ["supplierId"] = "S-7", ["result"] = "fail" }, InspectionSubject),
        new("doc-ins-3", new Dictionary<string, string?> { ["lot"] = "L-102", ["supplierId"] = "S-3", ["result"] = "pass" }, InspectionSubject),
    ];

    private static readonly (string RecordId, string Supplier, string Other)[] Expected =
    [
        ("doc-ins-1", "acme", "hanbit"),
        ("doc-ins-2", "acme", "hanbit"),
        ("doc-ins-3", "hanbit", "acme"),
    ];

    private sealed class Arm
    {
        public int Attempts;
        public int ParseFailures;
        public int ModelFailures;
        public int Inspections;
        public int RightLink;
        public int WrongLink;
        public int NoLink;
        public int SupplierDenotedByInspection;
        public int DemotionsMade;
    }

    [Fact]
    public async Task Measure_whether_a_record_key_lets_the_model_follow_a_reference_held_by_it()
    {
        var runs = int.TryParse(Environment.GetEnvironmentVariable("EYU_RECKEY_RUNS"), out var parsed) && parsed > 0 ? parsed : 4;
        var (httpClient, client) = EyuLlmLiveClient.Create();
        using var _ = httpClient;
        var proposer = new SinglePassOntologyProposer(client);
        var arms = new Dictionary<string, Arm> { ["unkeyed"] = new(), ["keyed"] = new() };

        for (var attempt = 0; attempt < runs; attempt++)
        {
            foreach (var name in new[] { "unkeyed", "keyed" })
            {
                var arm = arms[name];
                arm.Attempts++;
                OntologyProposal proposal;
                try
                {
                    proposal = await proposer.ProposeAsync(Declared, Records(name == "keyed"), TestContext.Current.CancellationToken);
                }
                catch (FormatException)
                {
                    arm.ParseFailures++;
                    continue;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !TestContext.Current.CancellationToken.IsCancellationRequested)
                {
                    arm.ModelFailures++;
                    continue;
                }

                Score(proposal, arm);
            }
        }

        var table = new StringBuilder()
            .AppendLine("| arm | attempts (parse failed · call failed) | inspections linked to the right supplier | to the wrong one | to none | supplier entity denoted by an inspection record | denotations demoted |")
            .AppendLine("|---|---|---|---|---|---|---|");
        foreach (var (name, arm) in arms)
        {
            table.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {arm.Attempts} ({arm.ParseFailures} · {arm.ModelFailures}) | {arm.RightLink}/{arm.Inspections} | {arm.WrongLink} | {arm.NoLink} | {arm.SupplierDenotedByInspection} | {arm.DemotionsMade} |");
        }

        output.WriteLine($"Prompt {SinglePassOntologyProposer.PromptFingerprint}, record keys {SinglePassOntologyProposer.RecordKeysPromptFingerprint}");
        output.WriteLine(table.ToString());
        if (Environment.GetEnvironmentVariable("EYU_RECKEY_REPORT") is { Length: > 0 } path)
        {
            await File.WriteAllTextAsync(path, $"Prompt {SinglePassOntologyProposer.PromptFingerprint}, record keys {SinglePassOntologyProposer.RecordKeysPromptFingerprint}\n\n{table}", new UTF8Encoding(false), TestContext.Current.CancellationToken);
        }

        Assert.True(arms.Values.Sum(a => a.Inspections) > 0, "at least one proposal must survive parsing for the measurement to mean anything");
    }

    private static void Score(OntologyProposal proposal, Arm arm)
    {
        static bool Is(EntityProposal e, string supplier) => e.Name.Contains(supplier, StringComparison.OrdinalIgnoreCase);
        var suppliers = proposal.Entities.Where(e => Is(e, "acme") || Is(e, "hanbit")).ToList();
        arm.DemotionsMade += proposal.DemotedDenotations.Count;
        arm.SupplierDenotedByInspection += suppliers.Count(s => s.DenotedBy.Any(r => r.StartsWith("doc-ins", StringComparison.Ordinal)));

        foreach (var (recordId, rightName, wrongName) in Expected)
        {
            arm.Inspections++;
            // The entities this inspection backs: everything citing it that is not a supplier.
            var backed = proposal.Entities
                .Where(e => !suppliers.Contains(e) && (e.Claim.Sources.Any(s => s.RecordId == recordId) || e.DenotedBy.Contains(recordId) || e.MentionedIn.Contains(recordId)))
                .Select(e => e.EntityId)
                .ToHashSet(StringComparer.Ordinal);
            string[] Linked(string supplier) => proposal.Relations
                .Where(r => (backed.Contains(r.FromEntityId) && suppliers.Any(s => Is(s, supplier) && s.EntityId == r.ToEntityId))
                         || (backed.Contains(r.ToEntityId) && suppliers.Any(s => Is(s, supplier) && s.EntityId == r.FromEntityId)))
                .Select(r => r.RelationName)
                .ToArray();
            var right = Linked(rightName).Length > 0;
            var wrong = Linked(wrongName).Length > 0;
            if (right && !wrong)
            {
                arm.RightLink++;
            }
            else if (wrong)
            {
                arm.WrongLink++;
            }
            else
            {
                arm.NoLink++;
            }
        }
    }
}
