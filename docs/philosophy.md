# Design Rationale

Why Eyu is shaped the way it is — the two questions this document answers that
the contract in [`README.md`](../README.md) states but does not justify:

1. Why must a caller's input take *form* (declared structure, or records
   from a source that at least identifies itself) before Eyu will judge it at
   all?
2. What kind of thing is the ontology Eyu proposes — a claim about the world,
   or something else?

> **Confidence key** — used throughout this document: 🟢 *strong anchor*
> (directly supported by cited literature or an evidenced precedent) · 🟡
> *moderate anchor* (the structural correspondence holds, but direct prior
> work is thin or the interpretation is contested in the source literature) ·
> 🔴 *weak / rejected* (the analogy does not hold literally; noted here only
> to record why it was rejected). Do not drop the marker when quoting a claim
> from this document elsewhere — an unmarked 🟡 reads as settled fact.

This document is about *Eyu's* layer of the stack — what it means for
declared structure and raw records to enter judgment, and what the resulting
proposal is. It does not extend to what a document *is* as an object; that
question belongs to the methodology layer upstream of Eyu and is answered on
its own terms there, independently of anything below.

---

## A. Why structure gates judgment 🟢

Eyu never judges an unstructured blob. It requires either declared structure
(`IStructureSource`) or records from a source that identifies itself well
enough to sample (`IRecordSample`) — some prior act of giving the input a
shape. This isn't an arbitrary implementation convenience; it mirrors a
general pattern in how any theory of cognition handles the raw/structured
divide: representations can only be evaluated against categories once they
have *some* form that makes categorical evaluation possible in the first
place. Kant's *Critique of Pure Reason* argues space and time function this
way for empirical intuition — they are the pure forms an intuition must
already have before any category can be applied to it (Transcendental
Aesthetic, A19–49/B33–73). Applied here: structure is the condition of
possibility for a judgment, not a judgment itself.

A second, independent line of support is sociological rather than
philosophical: Star & Griesemer's account of *boundary objects* ("Institutional
Ecology, 'Translations' and Boundary Objects", *Social Studies of Science*
19(3):387–420, 1989) identifies "standardized forms" as one of the
mechanisms that let heterogeneous groups collaborate without agreeing on
everything — weakly structured where everyone must agree, strongly
structured within each local use. That the gate can carry a thin, common
shape while still admitting rich, source-specific detail is not an
engineering coincidence; it's how this class of mechanism works elsewhere
too.

**Practical upshot:** don't relax `IStructureSource`/`IRecordSample` to
accept opaque, self-describing-less blobs "just this once" — that removes
the one thing that makes everything downstream a judgment about *something*
rather than a guess about *anything*.

## B. What kind of thing is Eyu's output 🟢

`IOntologyProposer` does not assert "this is what exists." It proposes
entities, relations, and confidence, always expressed through
`IGroundingContract` — `{claim, sources[], path[]}` — and a claim with no
sources cannot be expressed at all. That's a deliberate epistemological
stance, not just an audit-log feature.

A note on the word, because it is overloaded. *Grounding* here means the
citation itself: every claim points back at the source records it was read
from, and the check that is enforced is that those records exist and were
supplied to the call. It does **not** mean what LLM-based biomedical
extractors (OntoGPT and its relatives) call *ontology-based grounding* —
normalizing an extracted mention to a term identifier in a reference
ontology. Eyu performs no such normalization; a proposed entity type is a
label the model chose or the caller declared, not a lookup into a controlled
vocabulary. Read "grounded" throughout these documents as "traceable to
records", never as "resolved to a term id".

Gilles Kassel's *epistemic ontology* position — "we advocate the use of
'epistemic' ontologies, i.e., systems of categories representing our
knowledge of the world, rather than the world directly" ("A plea for
epistemic ontologies", *Applied Ontology* 18(4):367–397, 2023, DOI
10.3233/AO-230031) — names this stance directly and contrasts it with
*realist* upper ontologies such as BFO, which claim to describe "only
entities that exist." Robert B. Allen's "From Ontology to Structured Applied
Epistemology" (arXiv:1610.07241, 2016) frames the same shift as
representations that "make a claim" rather than merely "having" one — the
closest prior formulation to what `IGroundingContract` enforces at the type
level. This is also the footing the methodology layer upstream of Eyu —
[formology](https://github.com/iyulab/formology) — stands on: its own design
rationale reaches for the same epistemic-ontology position for the same
reason, so the lineage inside this stack runs formology → Eyu, one stance
inherited, not two independent arrivals at one paper.

The distinction matters operationally, not just rhetorically: a realist
ontology's job is to be *true*; an epistemic ontology's job is to be
*traceable*. Eyu is built for the second job. `IOntologyProposer`'s output
is never "the entity resolution is X" — it is "the evidence in these sources,
read through this path, supports X with this confidence." (The `path[]` slot
is part of that shape but is caller-supplied: Eyu's own proposer leaves it
empty, so today the traceability that is actually enforced is
claim-to-sources, not claim-to-reasoning-steps — see `IGroundingContract`.)
**Do not read
Eyu's proposals, or any consumer's persisted version of them, as direct
assertions about reality** — they are claims about what the source records
support, and they stop being meaningful the moment they're detached from
their sources.

## C. Innate layer vs. acquired layer — decided: exposed as proposal-level provenance 🟡

A judgment engine that starts from nothing has no vocabulary to propose
anything with. `IOntologyProposer` draws on two kinds of vocabulary: an
**innate layer** (a small, domain-independent vocabulary — Person,
Organization, Event, Location, Time, Action, and similar — fixed regardless
of which domain a caller is in) and an **acquired layer** (domain-specific
categories and accumulated pattern knowledge that only exist because a
particular caller's records have been seen before).

The innate layer anchors to **DOLCE** (Borgo, Ferrario, Gangemi, Guarino,
Masolo et al., "DOLCE: A Descriptive Ontology for Linguistic and Cognitive
Engineering", *Applied Ontology* 17(1), 2022; standardised as [ISO/IEC 21838-3:2023](https://www.iso.org/standard/78927.html),
*Top-level ontologies (TLO) — Part 3: DOLCE*), which explicitly frames its
categories as a "cognitive bias" — categories of thought about the world,
not categories of the world — rather than **BFO**. This is worth stating
plainly because it is easy to get backwards: BFO's own designer, Barry
Smith, describes the view that ontology represents "theories or languages
or concepts... not the world itself" as a position "inspired ... by Kant"
that BFO's realism explicitly rejects. Citing BFO as a Kantian-adjacent "a
priori categories" precedent would be citing the wrong side of a debate its
own author staged.

**Decided: the split is exposed, as a proposal-level `VocabularyOrigin`
tag (`Innate` | `Acquired`) on every entity/relation `IOntologyProposer`
returns — not as a sixth port.** The precedent set for entity resolution
(folded into `IOntologyProposer` as part of "core judgment," not split into
a separate port) still applies to *how* this is exposed — no new port, no
`ICommonOntologyPack`/`IDomainOntologyPack` surface. What changed from
"undecided" is narrower: whether the distinction should be visible to a
caller at all, and that is settled by §D, not by taste. §D already commits
Eyu's contract to "routing thresholds should be tuned against observed
precision/recall on held-out cases" — but innate-layer proposals (a small,
near-closed classification problem) and acquired-layer proposals (an
open-ended, sample-size-dependent problem that shifts as a caller's record
history grows) do not share a calibration curve. A caller cannot honor
§D's own requirement without knowing which regime a given confidence value
came from. Leaving the split invisible would make a promise the contract
elsewhere makes impossible to keep — so this is decided by consequence, not
preference: any caller doing confidence-based routing needs it, not just
one caller with a domain-specific reason to want it. Rejected: adding a
seventh/sixth port (`ICommonOntologyPack`-shaped) for this — pure
surface growth with no consumer need distinct from what a one-field tag
already answers.

## D. Confidence: routes, but only if calibrated 🟡

The core promise — "confidence routes, it doesn't decide" — is a form of
Kant's distinction between *determining* judgment (subsuming a particular
under an already-given universal) and *reflecting* judgment (searching for
a universal from a particular that doesn't yet have one) (*Critique of the
Power of Judgment*, Introduction IV, 5:179). High-confidence auto-apply
looks like determining judgment: the case is confidently subsumed under an
already-known category. Low-confidence draft-only looks like reflecting
judgment: nothing yet warrants confident subsumption, so a human is asked to
search for the right category instead of trusting the machine's guess.

This framing is offered as a heuristic, not a load-bearing claim — treat it
as an explanation of *why* three routing tiers make sense, not as evidence
that they're correctly calibrated.

**This is the one place in this document where the caveat is not optional.**
Self-reported model confidence is not trustworthy on its own. A published
case of fine-tuned local-model confidence collapsing to a near-constant
value (reported around 0.95, with an ECE near 0.174 and AUC-ROC near 0.500 —
i.e. no discriminative power at all) shows self-reported confidence can be
useless as a routing signal even while looking plausible. **Any
`IModelClient` implementation's confidence output must be treated as
uncalibrated unless that implementation states otherwise**, and routing
thresholds should be tuned against observed precision/recall on held-out
cases — never against the raw self-reported number alone. External
calibration (Platt scaling, temperature scaling, or an equivalent) is a
consumer- or `IModelClient`-implementation concern; Eyu's contract does not
mandate one specific technique, but it does mandate that raw confidence
never be trusted uncalibrated.

## E. Does Eyu become the thing the methodology argues against? 🟢

The methodology this stack is built on argues against requiring an ontology
designer as a precondition for using the system — against forcing every
project's structure through a translator who must first master ontology
vocabulary. It's fair to ask whether Eyu's innate layer (§C) reintroduces
exactly that: doesn't *someone* still have to design the categories?

The answer is no, and the reason is scope, not effort: the innate layer (if
adopted) is a fixed, minimal, domain-independent grammar — decided once,
shared across every domain, never touched by a project team. It answers
"what kind of thing counts as an entity or event at all," not "what does
*this* domain's data mean." Domain-specific structure — the part that
actually requires someone to know the domain — only ever grows from
accumulated records that have already passed through the structural gate
(§A) upstream. Nobody sits down to design an ontology before using the
system; the acquired layer is a consequence of use, not a precondition for
it. The innate layer is closer to a fixed piece of infrastructure (like
agreeing what a timestamp format is) than to the kind of ontology authorship
the methodology is arguing against.

## What this document deliberately does not claim 🔴

Recorded here so a future revision doesn't reintroduce them without
re-litigating why they were set aside:

- **Entity resolution is not framed as any philosophical analogy** (e.g. to
  the unity of apperception). It is folded into `IOntologyProposer` as
  ordinary engineering judgment — proposing what an entity *is* already
  implies deciding which records denote the same one. No stronger claim is
  made or needed.
- **Free-text-to-structured extraction is not "creative" in any strong
  sense.** Where a caller's declared structure already fixes what fields
  exist, filling them from unstructured text is closer to applying an
  already-given category to a particular case than to originating a new one
  — the interesting design act, if there is one, is choosing what fields the
  structure has in the first place, and that choice is made upstream of Eyu
  (by whoever declares the structure), not inside it.
