# Eval Rubric — Step 06: Story Writer

**Decision register item:** E-I-05  
**Status:** STAGED — deploy to `.github/eval-rubrics/step-06-story-writer.md`  
**Quality gate threshold:** 80% for PASS; 60–79% for CONDITIONAL; <60% for FAIL  

---

## Purpose

Evaluates the output of `06-story-writer.prompt.md` — backlog story quality, acceptance criteria fidelity, and implementation readiness.

---

## Criteria

| # | Criterion | Weight | Pass condition |
|---|---|---|---|
| 1 | **Coverage of approved scope** | 20% | Stories collectively cover approved specs/features with no major scope holes |
| 2 | **Story clarity and user value** | 14% | Stories are clearly written with actor, intent, and business value |
| 3 | **Acceptance criteria testability** | 20% | Acceptance criteria are specific, verifiable, and measurable |
| 4 | **NFR and compliance inclusion** | 14% | Relevant non-functional and compliance obligations are represented in story AC or notes |
| 5 | **Dependency and sequencing awareness** | 10% | Story dependencies/prerequisites are explicit enough to guide task breakdown |
| 6 | **Traceability to source requirements** | 12% | Story set maintains clear traceability to requirements/spec intent |
| 7 | **Implementation readiness quality** | 10% | Stories are structured to enable direct decomposition into engineering tasks |

**Total:** 100%

---

## Scoring Notes

- Criterion 3 is `FAIL` if acceptance criteria are vague or not objectively testable.
- Criterion 1 is `FAIL` if core approved scope is missing from the story set.
- Criterion 4 is `PARTIAL` when NFR/compliance concerns are mentioned but not anchored to criteria.
- If criterion 1 or 3 is `FAIL`, overall verdict cannot exceed `CONDITIONAL`.
