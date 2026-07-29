# Eval Rubric — Step 07: Task Breakdown

**Decision register item:** E-I-05  
**Status:** STAGED — deploy to `.github/eval-rubrics/step-07-task-breakdown.md`  
**Quality gate threshold:** 80% for PASS; 60–79% for CONDITIONAL; <60% for FAIL  

---

## Purpose

Evaluates the output of `07-task-breakdown.prompt.md` — implementation task decomposition, dependency mapping, and execution readiness.

---

## Criteria

| # | Criterion | Weight | Pass condition |
|---|---|---|---|
| 1 | **Story-to-task coverage** | 22% | Every approved story has complete task coverage with no unplanned implementation gaps |
| 2 | **Task atomicity and clarity** | 14% | Tasks are actionable, appropriately sized, and written as concrete engineering work items |
| 3 | **Dependency map validity** | 16% | Task dependencies are explicit, logically ordered, and free of unresolved circular blockers |
| 4 | **Acceptance criteria linkage** | 16% | Each task maps to acceptance criteria or test intent, enabling verification of completion |
| 5 | **Cross-functional completeness** | 12% | Tasks include code, tests, review/security obligations, and documentation where applicable |
| 6 | **Effort estimation quality** | 10% | Effort sizing is internally consistent and aligned to scope/complexity |
| 7 | **Execution risk visibility** | 10% | Key delivery risks and assumptions are captured with explicit mitigation or escalation notes |

**Total:** 100%

---

## Scoring Notes

- Criterion 1 is `FAIL` if any story is missing required implementation tasks.
- Criterion 3 is `FAIL` if dependency sequencing is contradictory or introduces deadlocks with no resolution path.
- Criterion 4 is `PARTIAL` when links exist but are too generic to verify completion.
- If criterion 1 is `FAIL`, overall verdict is `FAIL` regardless of weighted score.
