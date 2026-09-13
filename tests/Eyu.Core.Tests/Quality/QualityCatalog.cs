using Eyu.Core.Declared;
using Eyu.Core.Primitives;
using Eyu.Core.Records;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// One catalog domain the live measurements run over: a handful of real public-dataset records,
/// the competency questions a proposed structure over them should be able to answer, and the
/// declaration a form for those records would make — the full one, graded into an ablation
/// ladder. The questions and the declaration are properties of the domain, not of the library,
/// which is why the catalog lives in the test assembly and not in <c>Eyu.Core</c>.
/// </summary>
internal sealed record QualityCase(
    string Name,
    RawRecord[] Records,
    CompetencyQuestion[] Questions,
    DeclarationLadder Declaration);

/// <summary>
/// The fixed catalog every live measurement shares, so that two instruments run over the same
/// records and questions and their numbers are comparable. The baseline quality measurement
/// passes no declaration at all; the completeness ablation walks each case's ladder.
/// </summary>
internal static class QualityCatalog
{
    public static readonly QualityCase[] Cases =
    [
        // Real NRC Licensee Event Reports -- public US Nuclear Regulatory Commission filings.
        new("nuclear-power-ler",
        [
            new("0252022001", new Dictionary<string, string?>
            {
                ["plant_name"] = "Vogtle 3",
                ["event_date"] = "2022-10-06",
                ["title"] = "Automatic Reactor Trip Signal due to Inadequate Procedure Guidance Causing Incorrect Opening of Division B DC Supply Breaker",
            }),
            new("0252022002", new Dictionary<string, string?>
            {
                ["plant_name"] = "Vogtle 3",
                ["event_date"] = "2022-10-23",
                ["title"] = "Automatic Depressurization System Stage 4 Flow Paths Inoperable During Mode 6 with Upper Internals in Place due to Inadequate Work Processes",
            }),
            new("0252022003", new Dictionary<string, string?>
            {
                ["plant_name"] = "Vogtle 3",
                ["event_date"] = "2022-10-24",
                ["title"] = "Unborated Water Flowpath Not Secured per Technical Specification 3.9.2 due to Inadequate Procedure Revision",
            }),
        ],
        [
            new("Which reported events belong to a given plant?", ["plant|unit|site|facility|station", "event|report|occurrence"]),

            new("Which plant did a given event occur at?",
                ["plant|unit|site|facility|station", "event|report|occurrence", "occurred|reported|belongs|located|happened"]),
            new("Which events are attributed to a procedural or work-process deficiency?",
                ["procedure|process|guidance|work", "cause|deficiency|inadequate|reason"]),
        ],
        // Form order: each field, then the relation that field carries. An event report names its
        // plant, dates itself, and states in its title what happened and what it is attributed to.
        new(SubjectRef.Create("event"),
        [
            new DeclarationItem.Field(new DeclaredField("plant_name", "the plant the event is reported for", DeclaredValueKind.Text, Required: true)),
            new DeclarationItem.Relation(new DeclaredRelation("occurred_at", SubjectRef.Create("plant"), ViaField: "plant_name", DeclaredRelationKind.Reference)),
            new DeclarationItem.Field(new DeclaredField("event_date", "when the event occurred", DeclaredValueKind.Timestamp, Required: true)),
            new DeclarationItem.Field(new DeclaredField("title", "what happened and the deficiency it is attributed to", DeclaredValueKind.Text, Required: true)),
            new DeclarationItem.Relation(new DeclaredRelation("attributed_to", SubjectRef.Create("cause"), ViaField: "title", DeclaredRelationKind.Reference)),
            new DeclarationItem.Relation(new DeclaredRelation("deficient_procedure", SubjectRef.Create("procedure"), ViaField: "title", DeclaredRelationKind.Reference)),
        ])),
        // Real FAA Service Difficulty Reports -- public US Federal Aviation Administration data.
        new("aviation-sdr",
        [
            new("AALA202601050865", new Dictionary<string, string?>
            {
                ["aircraft_make"] = "BOEING",
                ["aircraft_model"] = "737823",
                ["part_name"] = "FLOORBEAM",
                ["part_condition"] = "CRACKED",
                ["discrepancy"] = "AIRCRAFT IN BASE MAINTENANCE: CRACK IN PASSENGER CABIN FLOORBEAM AT BS 540, RBL 2. REPLACED BREAK ASSEMBLY PANEL PER AARD 51-00-05-1.",
            }),
            new("AALA202601056048", new Dictionary<string, string?>
            {
                ["aircraft_make"] = "BOEING",
                ["aircraft_model"] = "737823",
                ["part_name"] = "FLOORBEAM",
                ["part_condition"] = "CRACKED",
                ["discrepancy"] = "AIRCRAFT IN BASE MAINTENANCE: CRACK IN PASSENGER CABIN FLOORBEAM AT BS 540, LBL 2. REPLACED BREAK ASSEMBLY PANEL PER AARD 51-00-05-1.",
            }),
            new("CALA2026010212586", new Dictionary<string, string?>
            {
                ["aircraft_make"] = "BOEING",
                ["aircraft_model"] = "767322",
                ["part_name"] = "SEAL",
                ["part_condition"] = "LEAKING",
                ["discrepancy"] = "NUMBER 1 ENG OIL QTY SLOWLY REDUCED TO 0 OVR 90 MINS ALL OTHER ENG IND. NORM AT THIS TIME. CAPT REPORTED THE OIL PRESSURE WAS 92 PSI UPON ENG SHUTDOWN.",
            }),
        ],
        [
            new("Which aircraft does a reported difficulty concern?",
                ["aircraft|airplane|plane|fleet", "report|difficulty|discrepancy|event|finding"]),
            new("Which part was found in what condition?",
                ["part|component|assembly", "condition|defect|damage|failure|crack|leak"]),
            new("Which reports describe the same part on the same aircraft model?",
                ["part|component|assembly", "aircraft|airplane|plane|model", "report|difficulty|discrepancy|event|finding"]),
        ],
        // Form order: the aircraft block, the part block, the condition, then the free text.
        new(SubjectRef.Create("difficulty_report"),
        [
            new DeclarationItem.Field(new DeclaredField("aircraft_make", "manufacturer of the aircraft", DeclaredValueKind.Text)),
            new DeclarationItem.Field(new DeclaredField("aircraft_model", "model of the aircraft the difficulty was found on", DeclaredValueKind.Text, Required: true)),
            new DeclarationItem.Relation(new DeclaredRelation("concerns_aircraft", SubjectRef.Create("aircraft"), ViaField: "aircraft_model", DeclaredRelationKind.Reference)),
            new DeclarationItem.Field(new DeclaredField("part_name", "the part the difficulty was found on", DeclaredValueKind.Text, Required: true)),
            new DeclarationItem.Relation(new DeclaredRelation("reports_part", SubjectRef.Create("part"), ViaField: "part_name", DeclaredRelationKind.Reference)),
            new DeclarationItem.Field(new DeclaredField("part_condition", "the condition the part was found in", DeclaredValueKind.Text)),
            new DeclarationItem.Relation(new DeclaredRelation("found_in_condition", SubjectRef.Create("condition"), ViaField: "part_condition", DeclaredRelationKind.Reference)),
            new DeclarationItem.Field(new DeclaredField("discrepancy", "free-text description of the difficulty", DeclaredValueKind.Text)),
        ])),
    ];
}
