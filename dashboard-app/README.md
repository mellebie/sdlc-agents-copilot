# Pipeline Dashboard Next App

This folder contains a read-only Next.js dashboard for the SDLC pipeline.

## Run

```bash
npm install
npm run dev
```

The app reads pipeline artifacts from the parent repository root, including:

- `outputs/pipeline-manifest.json`
- `outputs/eval-summary.md`
- `outputs/task-log.md`
- `decisions/guardrails-evals-governance.md`
- `scripts/Validate-PipelineInputs.ps1`
- `scripts/Invoke-PipelineEval.ps1`
- `scripts/Invoke-PipelineEval-AutoGate.ps1`
- `scripts/Write-PipelineManifest.ps1`

## Eval and Rubric Visibility

The dashboard surfaces both deterministic quality gates and rubric orchestration from `outputs/eval-summary.md`, including:

- strict mode status
- rubric auto-eval mode
- rubric result totals (executed/pass/conditional/fail)
- per-artifact rubric verdict and confidence when available

## Context Pack Relationship

The dashboard does not read files under `context/` directly.
Instead, context standards influence the dashboard through generated artifacts
(`outputs/task-log.md`, `outputs/review-findings.md`, `outputs/security-findings.md`, and eval reports)
that include context standards evidence and divergence notes.
