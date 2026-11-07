using System.Text;
using System.Text.Json;
using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using Xunit;

namespace Eyu.Rdf.Tests.Measurement;

/// <summary>
/// Writes the Turtle this writer produces for a fixed corpus of awkward names — every file with the names
/// that went into it — so an implementation that shares no code with this one can read them back. The
/// unit tests of <see cref="OntologyTurtle"/> already do that with dotNetRDF, and a second parser is worth
/// having because two parsers are wrong about the same line only if they were written alike. The reading
/// side is any independent Turtle parser: it should parse every file, and every label should come back as
/// the Unicode Normalization Form C of the name that was written (the writer promises NFC text).
/// <para>
/// The corpus is made of names, never of anyone's records. It is the same on every run, so a change in
/// what the reading side counts is a change in the writer. Excluded from the default build
/// (<c>-p:IncludeEyuBenchmarks=true</c>); <c>EYU_RDF_CORPUS_DIR</c> names the directory to write into.
/// </para>
/// </summary>
public class IndependentParserCorpusEmitter
{
    private static readonly RdfExportOptions Options = new(new Uri("https://example.org/plant#"));

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>One line per case: what the writer is asked to carry that a hand-made grammar tends to miss.</summary>
    private static readonly (string Case, string[] Names)[] Corpus =
    [
        ("ascii-plain", ["Pump P-101", "Line 3", "Kim"]),
        ("double-quote", ["The \"main\" pump", "5\" valve"]),
        ("single-quote", ["O'Brien", "rock 'n' roll"]),
        ("backslash", ["one\\two\\three", "a\\nb", "trailing\\"]),
        ("newline-and-tab", ["two\nlines", "col1\tcol2", "cr\rlf\r\n"]),
        ("control-characters", ["bell\u0007", "nul-free\u0001\u001f", "del\u007f"]),
        ("hangul-composed", ["한글 이름", "유량계 FT-101", "펌프 3호기"]),
        ("hangul-decomposed", ["한글 이름", "유량계 FT-101", "펌프 3호기"]),
        ("hangul-jamo-only", ["ㅍㅓㅁㅍㅡ", "각"]),
        ("latin-accents-composed", ["Caf\u00e9", "Z\u00fcrich", "na\u00efve"]),
        ("latin-accents-decomposed", ["Cafe\u0301", "Zu\u0308rich", "nai\u0308ve"]),
        ("combining-marks-stacked", ["a\u0323\u0302", "o\u0302\u0323", "e\u0301\u0323"]),
        ("angstrom-and-kelvin", ["\u212b", "\u212a elvin", "\u2126"]),
        ("fullwidth-and-compat", ["\uff21\uff22\uff23", "\u2460 first", "\ufb01 ligature"]),
        ("astral-emoji", ["pump \U0001F6E0", "\U0001F600\U0001F601", "x\U0001F9D1\u200D\U0001F527"]),
        ("astral-ideographs", ["\U00020BB7", "\U0002F800"]),
        ("right-to-left", ["\u0645\u0636\u062e\u0629", "\u05de\u05e9\u05d0\u05d1\u05d4 3"]),
        ("zero-width-and-bidi", ["a\u200Bb", "x\u202Ey", "\u2066z\u2069"]),
        ("iri-unsafe-characters", ["a b", "a<b>c", "a{b}c", "a|b", "a^b", "a`b", "a%20b"]),
        ("iri-delimiters", ["a#b", "a/b", "a:b", "a?b", "a@b", "a;b,c"]),
        ("leading-digit-and-symbol", ["3rd line", "-dash", ".dot", "_under", "~tilde"]),
        ("punctuation-only", ["!!!", "???", "..."]),
        ("whitespace-edges", [" leading", "trailing ", "  both  ", "\u00a0nbsp\u00a0"]),
        ("very-long-name", [new string('x', 4000), string.Concat(Enumerable.Repeat("펌프", 1500))]),
        ("many-entities", [.. Enumerable.Range(1, 60).Select(i => $"Equipment {i:D3}")]),
        ("duplicate-spellings", ["Pump", "pump", "PUMP", "Pump "]),
        ("turtle-keywords", ["a", "true", "false", "@prefix", "BASE", "PREFIX"]),
        ("prefix-lookalikes", ["rdf:type", "owl:Thing", "eyu:iriRule", "http://example.org/x"]),
        ("numeric-lookalikes", ["1", "1.5", "-2", "1e10", "0x1F"]),
        ("language-tag-lookalikes", ["pump@en", "\"pump\"@en", "\"1\"^^xsd:integer"]),
        ("comment-and-bracket", ["# not a comment", "[blank]", "(list)", "<iri>"]),
        ("unicode-whitespace", ["a\u2003b", "a\u3000b", "a\u2028b", "a\u2029b"]),
        ("private-use-and-noncharacter", ["\ue000", "\uf8ff", "a\ufffdb"]),
        ("mixed-script", ["Pump 펌프 ポンプ 泵 насос", "ΔP-101", "µm filter"]),
        ("single-character", ["a", "가", "\U0001F6E0"]),
        ("repeated-id-prefix", ["e1", "e10", "e100"]),
        ("hangul-with-ascii-edges", ["(주)이유랩", "No.1 라인", "A동-3층"]),
    ];

    [Fact]
    public void Write_the_corpus_for_the_independent_parser_check()
    {
        var dir = Environment.GetEnvironmentVariable("EYU_RDF_CORPUS_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            Assert.Skip("EYU_RDF_CORPUS_DIR is not set — the corpus is written for an outside parser to read.");
        }

        Directory.CreateDirectory(dir);
        var cases = new List<object>();
        foreach (var (name, names) in Corpus)
        {
            // The hangul-decomposed case is the composed one spelled in Normalization Form D: the same
            // names, a different byte sequence, which is what a source that decomposes hands the writer.
            var spelled = name == "hangul-decomposed" ? names.Select(n => n.Normalize(NormalizationForm.FormD)).ToArray() : names;

            var entities = spelled.Select((n, i) => EntityProposal.Create(
                $"e{i + 1}", n, "Equipment",
                GroundedClaim.Create($"{n} is a piece of equipment", [new SourceRef($"r-{i + 1}")]),
                InnateVocabulary.OfEntityType("Equipment"), 0.8)).ToList();
            var proposal = new OntologyProposal(entities, [], []);

            var file = $"{name}.ttl";
            File.WriteAllText(Path.Combine(dir, file), OntologyTurtle.ToTurtle(proposal, Options), new UTF8Encoding(false));
            cases.Add(new
            {
                file,
                names = spelled,
                individualIris = OntologyTurtle.IndividualIris(proposal, Options).Values.ToArray(),
            });
        }

        File.WriteAllText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(new { writer = "Eyu.Rdf OntologyTurtle", cases }, ManifestJson),
            new UTF8Encoding(false));

        Assert.Equal(Corpus.Length, cases.Count);
    }
}
