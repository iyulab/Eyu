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
/// One document the live quality measurement runs over in the other regime a caller can pick:
/// the records are chunks of one document, which name many entities and denote none
/// (<see cref="Eyu.Core.Linkage.LinkageOptions.RecordsDenoteEntities"/> <c>false</c>). A document
/// has no form behind it, so there is no declaration ladder, and no record pair is compared, so
/// the record-linkage pre-filter has nothing to report — which is why these cases are kept apart
/// from <see cref="QualityCase"/> rather than mixed into the same tables. What a caller holding
/// such documents can declare is the vocabulary it expects them to use —
/// <paramref name="Vocabulary"/>, the entity types and typed relations, by name alone — and the
/// measurement runs each document both without it and with it.
/// </summary>
internal sealed record DocumentCase(
    string Name,
    RawRecord[] Chunks,
    CompetencyQuestion[] Questions,
    DeclaredStructure[] Vocabulary);

/// <summary>
/// The fixed catalog every live measurement shares, so that two instruments run over the same
/// records and questions and their numbers are comparable. The baseline quality measurement
/// passes no declaration at all; the completeness ablation walks each case's ladder.
/// <see cref="DocumentCases"/> is measured by the baseline only, in the document regime, once with
/// no declaration and once with the case's declared vocabulary; adding it left <see cref="Cases"/>
/// as it was, so runs before and after still compare on those.
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
    // Company-profile documents about fictional Korean companies, written for this catalog: a
    // consumer's documents cannot be published, and the shape — a profile split into a handful of
    // sections naming partners, customers, products, places and dates — is what matters. The
    // questions are written from what such a profile is read for, not from any model's output.
    public static readonly DocumentCase[] DocumentCases =
    [
        new("company-profile-single-chunk",
        [
            Chunk("a-c1", "한빛테크는 2015년에 설립된 산업용 센서 데이터 분석 기업이다. 대표이사는 이서준이다. 주요 파트너사로는 누리전자와 세온시스템이 있으며, 세온시스템과는 예지보전 플랫폼을 공동 개발했다. 주요 고객사는 동해발전과 청운제철이다. 자체 개발 솔루션의 이름은 '센티넬 인사이트'이며 동해발전이 이 솔루션을 발전설비 감시에 사용하고 있다."),
        ],
        [
            PartnersAndCustomers,
            new("Which customer uses which of the company's products?",
                ["customer|client|buyer", "product|solution|software|platform|service|system", "uses|usage|using|adopt|deploy|operat"]),
            new("Which people belong to which organization?",
                ["person|people|individual|employee|executive|staff|member", "organization|organisation|company|firm|enterprise|business|corporation"]),
        ],
        CompanyProfileVocabulary),
        new("company-profile-document-chunks",
        [
            Chunk("b-c1", "1. 회사 개요\n가람솔루션은 2012년 대전에서 설립되었다. 본사는 대전 유성구에 있으며 연구소는 판교에 있다. 대표이사는 박도윤이다."),
            Chunk("b-c2", "2. 사업 분야\n가람솔루션은 스마트팩토리용 공정 모니터링 소프트웨어와 설비 이상 탐지 서비스를 제공한다. 2018년부터 클라우드 기반 서비스로 전환했다."),
            Chunk("b-c3", "3. 파트너십\n가람솔루션의 주요 파트너사는 한결네트웍스와 미리내클라우드이다. 미리내클라우드와는 2020년 전략적 제휴를 맺고 호스팅 인프라를 공동 운영한다."),
            Chunk("b-c4", "4. 주요 고객\n주요 고객사는 서진화학, 태성모터스, 부강식품이다. 서진화학은 울산 공장에 가람솔루션의 공정 모니터링 소프트웨어를 도입했다."),
            Chunk("b-c5", "5. 자체 솔루션\n자체 개발 솔루션은 '가람 옵저버'와 '가람 프레딕트'이다. 가람 프레딕트는 가람 옵저버의 데이터를 이용해 설비 고장을 예측한다."),
            Chunk("b-c6", "6. 연혁\n2012년 대전에서 창업, 2016년 판교 연구소 개소, 2020년 미리내클라우드와 제휴, 2023년 태성모터스와 3년 공급 계약 체결."),
            Chunk("b-c7", "7. 조직\n기술총괄은 최하린이며 이전에 한결네트웍스에서 근무했다. 영업본부장 정우진은 서진화학 프로젝트를 책임지고 있다."),
        ],
        [
            PartnersAndCustomers,
            new("Where are the company's headquarters and research lab located?",
                ["office|headquarter|laborator|research|facility|branch|campus", "location|place|city|region|address|located"]),
            new("What happened in which year of the company's history?",
                ["event|milestone|history|founding|founded|contract|agreement|alliance|opening", "year|date|time|when|period"]),
        ],
        CompanyProfileVocabulary),
    ];

    // What a caller holding company profiles would declare: the kinds of thing it expects them to
    // name and the roles it needs told apart -- a partner is not a customer, and an employee is
    // neither -- by name alone, since a document has no form to declare fields from. Deliberately
    // silent on places and dates, which stay the model's to find.
    private static DeclaredStructure[] CompanyProfileVocabulary =>
    [
        new(SubjectRef.Create("Organization"), [],
        [
            new DeclaredRelation("PartnerOf", SubjectRef.Create("Organization")),
            new DeclaredRelation("CustomerOf", SubjectRef.Create("Organization")),
            new DeclaredRelation("Uses", SubjectRef.Create("Product")),
        ]),
        new(SubjectRef.Create("Person"), [], [new DeclaredRelation("EmployedBy", SubjectRef.Create("Organization"))]),
        new(SubjectRef.Create("Product"), [], []),
    ];

    /// <summary>
    /// The catalog case names <c>EYU_LLM_QUALITY_CASES</c> selects (comma-separated), or
    /// <c>null</c> for all of them — read by every live instrument over this catalog, so one prompt
    /// change can be re-measured on the cases it touches. A name that matches no case fails the run rather than measuring
    /// nothing under a report that looks complete.
    /// </summary>
    public static HashSet<string>? SelectedCaseNames()
    {
        var value = Environment.GetEnvironmentVariable("EYU_LLM_QUALITY_CASES");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var names = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        var known = Cases.Select(c => c.Name).Concat(DocumentCases.Select(c => c.Name)).ToHashSet(StringComparer.Ordinal);
        var unknown = names.Where(n => !known.Contains(n)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"EYU_LLM_QUALITY_CASES names no catalog case: {string.Join(", ", unknown)}. Known cases: {string.Join(", ", known)}.");
        }

        return names;
    }

    private static CompetencyQuestion PartnersAndCustomers => new(
        "Which organizations are the company's partners, and which are its customers?",
        ["organization|organisation|company|firm|enterprise|business|corporation", "partner|alliance|collaborat", "customer|client|buyer"]);

    // A document chunk as a caller holding text would pass it: the text under one field.
    private static RawRecord Chunk(string id, string text) => new(id, new Dictionary<string, string?> { ["content"] = text });
}
