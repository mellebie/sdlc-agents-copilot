# Eval Rubric — Step 05: Risk Assessment

**Decision register item:** E-I-05  
**Status:** STAGED — deploy to `.github/eval-rubrics/step-05-risk-assessment.md`  
**Quality gate threshold:** 80% for PASS; 60–79% for CONDITIONAL; <60% for FAIL  

---

## Purpose

Evaluates the output of `05-risk-assessment.prompt.md` — risk identification quality, mitigation rigor, and go/no-go decision clarity.

---

## Criteria

| # | Criterion | Weight | Pass condition |
|---|---|---|---|
| 1 | **Risk register completeness** | 20% | Risks cover architecture, delivery, security/compliance, and operational concerns relevant to scope |
| 2 | **Severity and likelihood quality** | 14% | Risk ratings are explicit, internally consistent, and tied to concrete impact descriptions |
| 3 | **Mitigation specificity** | 18% | Each material risk has actionable mitigation and a clear owner or execution path |
| 4 | **Detection and monitoring approach** | 12% | Early warning indicators or monitoring signals are defined for high-impact risks |
| 5 | **Dependency and assumption exposure** | 10% | External dependencies and assumptions are surfaced with risk implications |
| 6 | **Decision readiness (GO/NO-GO)** | 16% | Recommendation is explicit and justified by risk posture, not stated without rationale |
| 7 | **Residual risk transparency** | 10% | Residual risks after mitigation are acknowledged with contingency or escalation guidance |

**Total:** 100%

---

## Scoring Notes

- Criterion 3 is `FAIL` if mitigations are generic statements with no actionable next step.
- Criterion 6 is `FAIL` if GO/NO-GO recommendation is missing or unsupported by evidence.
- Criterion 4 is `PARTIAL` when monitoring intent exists but lacks concrete trigger conditions.
- If criterion 6 is `FAIL`, overall verdict cannot exceed `CONDITIONAL`.
