# Eval Rubric — Step 04: Architecture

**Decision register item:** E-I-05  
**Status:** STAGED — deploy to `.github/eval-rubrics/step-04-architecture.md`  
**Quality gate threshold:** 80% for PASS; 60–79% for CONDITIONAL; <60% for FAIL  

---

## Purpose

Evaluates the output of `04-architecture.prompt.md` — architecture design, data flow, and technical decision rationale.

---

## Criteria

| # | Criterion | Weight | Pass condition |
|---|---|---|---|
| 1 | **Component model completeness** | 16% | Core components, responsibilities, and boundaries are explicit and consistent |
| 2 | **Data flow correctness** | 16% | End-to-end flows are coherent, identify trust boundaries, and map to intended behavior |
| 3 | **Decision rationale and trade-offs** | 16% | Major architecture choices include rationale and at least one considered alternative or trade-off |
| 4 | **Risk-to-mitigation linkage** | 14% | Architectural risks are paired with mitigation or control strategy, not just listed |
| 5 | **Security and compliance controls** | 16% | Authentication, authorization, data protection, and compliance controls are concrete and enforceable |
| 6 | **Operability and reliability design** | 12% | Observability, failure handling, and resilience expectations are defined at architecture level |
| 7 | **Technology stack justification** | 10% | Selected technologies are justified against constraints and non-functional requirements |

**Total:** 100%

---

## Scoring Notes

- Criterion 3 is `FAIL` if architecture choices are asserted without any reasoning or trade-off analysis.
- Criterion 5 is `FAIL` if security controls are generic statements with no implementation intent.
- Criterion 4 is `PARTIAL` when risks are present but mitigations are vague or non-actionable.
- If criteria 2 or 5 is `FAIL`, overall verdict cannot exceed `CONDITIONAL`.
