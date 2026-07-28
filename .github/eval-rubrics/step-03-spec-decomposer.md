# Eval Rubric — Step 03: Spec Decomposer

**Decision register item:** E-I-05  
**Status:** STAGED — deploy to `.github/eval-rubrics/step-03-spec-decomposer.md`  
**Quality gate threshold:** 80% for PASS; 60–79% for CONDITIONAL; <60% for FAIL  

---

## Purpose

Evaluates the output of `03-spec-decomposer.prompt.md` — detailed technical specifications derived from requirements and clarifications.

---

## Criteria

| # | Criterion | Weight | Pass condition |
|---|---|---|---|
| 1 | **Requirements-to-spec traceability** | 22% | Every functional requirement has at least one traceable specification entry with stable identifiers |
| 2 | **No silent invention** | 18% | New behavior, constraints, or data elements not grounded in upstream artifacts are flagged with `[ASSUMPTION: ...]` or `[GAP: ...]` |
| 3 | **Ambiguity handling quality** | 12% | Ambiguous requirement areas are explicitly carried forward with actionable unresolved items, not hidden |
| 4 | **Non-functional specification depth** | 16% | Security, compliance, performance, and reliability expectations are stated in testable terms |
| 5 | **Dependency and interface completeness** | 12% | External dependencies and system boundaries are clearly listed with direction of interaction and key contract notes |
| 6 | **Contradiction detection** | 10% | No internal contradictions between functional and non-functional sections, or contradictions are explicitly flagged |
| 7 | **Spec testability** | 10% | Specifications are measurable/verifiable and can be turned into concrete test cases without reinterpretation |

**Total:** 100%

---

## Scoring Notes

- Criterion 1 is `FAIL` if any major requirement area has no corresponding spec coverage.
- Criterion 2 is `FAIL` if untraceable requirements are presented as fact without an explicit `[ASSUMPTION: ...]` or `[GAP: ...]` marker.
- Criterion 6 requires explicit call-out of conflicts when detected; unresolved contradiction hidden in prose is `FAIL`.
- If criteria 1 or 2 is `FAIL`, overall verdict cannot exceed `CONDITIONAL`.
