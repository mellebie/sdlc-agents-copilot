import { loadDashboardSnapshot } from '../lib/dashboard-data';
import type { PipelineSnapshot } from '../lib/dashboard-data';
import SdlcPhaseFlow from './components/SdlcPhaseFlow';

function toneLabel(status: string): string {
  switch (status) {
    case 'done':
      return 'Complete';
    case 'warn':
      return 'Conditions open';
    case 'err':
      return 'Blocked';
    default:
      return 'Pending';
  }
}

function toneClass(status: string): string {
  switch (status) {
    case 'done':
      return 'done';
    case 'warn':
      return 'warn';
    case 'err':
      return 'err';
    default:
      return 'pend';
  }
}

function shortStatus(status: string): string {
  switch (status) {
    case 'done':
      return 'done';
    case 'warn':
      return 'warn';
    case 'err':
      return 'error';
    default:
      return 'pending';
  }
}

function confidenceBandFromPercent(percent: number): 'Low' | 'Medium' | 'High' {
  if (percent >= 75) {
    return 'High';
  }

  if (percent >= 40) {
    return 'Medium';
  }

  return 'Low';
}

function evalTone(gate: string): 'done' | 'warn' | 'err' | 'pend' {
  const normalized = gate.toLowerCase();

  if (normalized.includes('fail')) {
    return 'err';
  }

  if (normalized.includes('conditional')) {
    return 'warn';
  }

  if (normalized.includes('pass')) {
    return 'done';
  }

  return 'pend';
}

function rubricTone(artifact: PipelineSnapshot['evalSummary']['artifacts'][number]): 'done' | 'warn' | 'err' | 'pend' {
  if (artifact.rubric.status === 'NOT_CONFIGURED') {
    return 'pend';
  }

  if (artifact.rubric.status === 'NOT_EXECUTED') {
    return 'warn';
  }

  if (artifact.rubric.verdict === 'FAIL') {
    return 'err';
  }

  if (artifact.rubric.verdict === 'CONDITIONAL') {
    return 'warn';
  }

  if (artifact.rubric.verdict === 'PASS') {
    return 'done';
  }

  return 'pend';
}

function rubricSummaryTone(summary: PipelineSnapshot['evalSummary']): 'done' | 'warn' | 'err' {
  if (summary.rubricCounts.fail > 0) {
    return 'err';
  }

  if (summary.rubricCounts.conditional > 0 || summary.rubricAutoEval.toUpperCase() !== 'ENABLED') {
    return 'warn';
  }

  return 'done';
}

function rubricLabel(artifact: PipelineSnapshot['evalSummary']['artifacts'][number]): string {
  if (artifact.rubric.status === 'NOT_CONFIGURED') {
    return 'not configured';
  }

  if (artifact.rubric.status === 'NOT_EXECUTED') {
    return 'not executed';
  }

  const verdict = artifact.rubric.verdict?.toLowerCase() ?? 'executed';
  if (artifact.rubric.confidencePercent === null) {
    return verdict;
  }

  return `${verdict} (${artifact.rubric.confidencePercent}%)`;
}

function artifactIssueSummary(artifact: PipelineSnapshot['evalSummary']['artifacts'][number]): string[] {
  const issues: string[] = [];
  const missingSections = artifact.requiredSections
    .filter((section) => !section.present)
    .map((section) => section.section);
  const flagIssues = Object.entries(artifact.flags)
    .filter(([, count]) => count > 0)
    .map(([flagName, count]) => `${flagName} ${count}`);

  if (missingSections.length > 0) {
    issues.push(`Missing sections: ${missingSections.join(', ')}`);
  }

  if (flagIssues.length > 0) {
    issues.push(`Flags: ${flagIssues.join(', ')}`);
  }

  if (issues.length === 0) {
    issues.push('No missing sections or flags detected.');
  }

  return issues;
}

function governanceTone(status: string): 'done' | 'warn' | 'err' | 'pend' {
  const normalized = status.toUpperCase();

  if (normalized.startsWith('IMPLEMENTED') || normalized.startsWith('RESOLVED')) {
    return 'done';
  }

  if (normalized.startsWith('OPEN')) {
    return 'warn';
  }

  return 'pend';
}

function formatUsd(value: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0
  }).format(value);
}

export default function DashboardPage() {
  const data = loadDashboardSnapshot();
  const isStepScopedEval = /^step\s+.+\s+only$/i.test(data.evalSummary.scope);
  const awaitingActions = data.steps.filter((step) => step.status !== 'done' || !step.agentRan);
  const nextAction = awaitingActions[0] ?? null;
  const actionQueuePreview = awaitingActions.slice(0, 4);
  const remainingActionCount = Math.max(awaitingActions.length - actionQueuePreview.length, 0);
  const progressPercent = Math.round((data.stats.stepsCompleted / data.stats.stepsTotal) * 100);
  const overallConfidencePercent = data.steps.length
    ? Math.round(data.steps.reduce((sum, step) => sum + step.confidence.percent, 0) / data.steps.length)
    : 0;
  const overallConfidenceBand = confidenceBandFromPercent(overallConfidencePercent);
  const highConfidenceStages = data.steps.filter((step) => step.confidence.band === 'High').length;
  const mediumConfidenceStages = data.steps.filter((step) => step.confidence.band === 'Medium').length;
  const lowConfidenceStages = data.steps.filter((step) => step.confidence.band === 'Low').length;
  const lowestConfidenceStep = [...data.steps].sort((left, right) => left.confidence.percent - right.confidence.percent)[0] ?? null;
  const confidenceAttentionCount = data.evalSummary.failCount + data.governance.openCount + lowConfidenceStages;
  const confidenceScoringHelp = [
    'Confidence scoring (starts at 100):',
    'warn status: -20',
    'blocked status: -55',
    'pending status: -70',
    'missing input artifact: -16 each',
    'missing output artifact: -12 each',
    'no agent run evidence: -20',
    'checkpoint not done: -8'
  ].join('\n');
  const blockerHighlights = [
    ...(data.evalSummary.artifacts.filter((artifact) => artifact.gate === 'FAIL').map((artifact) => `${artifact.file}: ${artifact.gate}`)),
    ...(data.governance.items.filter((item) => item.status.startsWith('OPEN')).slice(0, 3).map((item) => `${item.id} ${item.status}`))
  ];
  const governanceItems = [...data.governance.items].sort((left, right) => {
    const rank = (status: string): number => {
      const normalized = status.toUpperCase();

      if (normalized.startsWith('OPEN')) {
        return 0;
      }

      if (normalized.startsWith('RESOLVED')) {
        return 1;
      }

      if (normalized.startsWith('IMPLEMENTED')) {
        return 2;
      }

      return 3;
    };

    return rank(left.status) - rank(right.status) || left.id.localeCompare(right.id);
  });
  const sortedEvalArtifacts = [...data.evalSummary.artifacts].sort((left, right) => {
    const rank = (gate: string): number => {
      const normalized = gate.toLowerCase();

      if (normalized.includes('fail')) {
        return 0;
      }

      if (normalized.includes('conditional')) {
        return 1;
      }

      if (normalized.includes('missing')) {
        return 2;
      }

      return 3;
    };

    return rank(left.gate) - rank(right.gate) || left.file.localeCompare(right.file);
  });
  const stepById = new Map(data.steps.map((step) => [step.step, step]));

  const toCheckpointTone = (phaseStarted: boolean, passed: boolean): 'done' | 'warn' | 'pend' => {
    if (passed) {
      return 'done';
    }

    return phaseStarted ? 'warn' : 'pend';
  };

  const summarizePhase = (statuses: string[]): 'done' | 'warn' | 'err' | 'pend' => {
    if (statuses.some((status) => status === 'err')) {
      return 'err';
    }

    if (statuses.some((status) => status === 'warn')) {
      return 'warn';
    }

    if (statuses.every((status) => status === 'done')) {
      return 'done';
    }

    if (statuses.every((status) => status === 'pend')) {
      return 'pend';
    }

    return 'warn';
  };

  const sdlcPhases = [
    {
      id: 'initiate',
      title: 'Initiate',
      subtitle: 'Business intake and PRD baseline',
      stageIds: ['0', '00'],
      checkpointIndexes: [0]
    },
    {
      id: 'analyze',
      title: 'Analyze',
      subtitle: 'Requirements and clarification hardening',
      stageIds: ['01', '02'],
      checkpointIndexes: [1]
    },
    {
      id: 'design',
      title: 'Design',
      subtitle: 'Specification, architecture, and risk model',
      stageIds: ['03', '04', '05'],
      checkpointIndexes: [2]
    },
    {
      id: 'build',
      title: 'Build',
      subtitle: 'Stories, tasks, and implementation',
      stageIds: ['06', '07', '08'],
      checkpointIndexes: [3]
    },
    {
      id: 'validate-release',
      title: 'Validate & Release',
      subtitle: 'Testing, review, security, docs, and PR handoff',
      stageIds: ['09', '10', '11', '12', '13'],
      checkpointIndexes: [4]
    }
  ].map((phase) => {
    const stages = phase.stageIds
      .map((stageId) => stepById.get(stageId))
      .filter((stage): stage is NonNullable<typeof stage> => Boolean(stage));
    const phaseStarted = stages.some((stage) => stage.status !== 'pend');
    const checkpoints = phase.checkpointIndexes
      .map((checkpointIndex) => {
        const checkpoint = data.checkpoints[checkpointIndex];
        if (!checkpoint) {
          return null;
        }

        return {
          id: `CP${checkpointIndex}`,
          ...checkpoint,
          tone: toCheckpointTone(phaseStarted, checkpoint.passed)
        };
      })
      .filter((checkpoint): checkpoint is NonNullable<typeof checkpoint> => Boolean(checkpoint));

    const status = summarizePhase(stages.map((stage) => stage.status));
    const confidencePercent = stages.length
      ? Math.round(stages.reduce((sum, stage) => sum + stage.confidence.percent, 0) / stages.length)
      : 0;
    const doneCount = stages.filter((stage) => stage.status === 'done').length;
    const warnCount = stages.filter((stage) => stage.status === 'warn').length;
    const errCount = stages.filter((stage) => stage.status === 'err').length;
    const pendCount = stages.filter((stage) => stage.status === 'pend').length;

    const confidenceDrivers = [
      `${doneCount}/${stages.length} stage(s) are done.`,
      warnCount > 0 ? `${warnCount} stage(s) are draft/conditional.` : 'No stage is in warning state.',
      errCount > 0 ? `${errCount} stage(s) are blocked.` : 'No blocked stages in this phase.',
      pendCount > 0 ? `${pendCount} stage(s) are pending execution.` : 'No pending stages in this phase.'
    ];

    return {
      ...phase,
      status,
      stages,
      checkpoints,
      confidence: {
        percent: confidencePercent,
        band: confidenceBandFromPercent(confidencePercent),
        drivers: confidenceDrivers
      }
    };
  });

  return (
    <main className="shell">
      <section className="section-block">
        <section className="hero">
          <div className="hero-main">
            <div className="hero-badges">
              <div className="eyebrow">AI First SDLC Pipelie Dashboard</div>
              <div
                className="mode-badge"
                title="Strict gating enabled: DRAFT stays warning, no downstream auto-approval, and checkpoints pass only when required stages are complete."
              >
                Strict Gating
              </div>
            </div>
            <h1>{data.project.name}</h1>
            <p className="lede">
              A single operational view for pipeline progress, guardrails, governance, evals, and delivery readiness.
            </p>
          </div>
          <div className="hero-meta">
            <div className="hero-card">
              <span className="meta-label">Client</span>
              <strong>{data.project.client}</strong>
              <span className="meta-sub">Branch: {data.project.branch}</span>
            </div>
          </div>
          <div className="hero-insight-grid">
            <section className="roi-block" aria-label="ROI insights">
              <div className="roi-head">
                <div className="section-kicker">ROI</div>
                <h2>AI execution leverage</h2>
                <p>
                  Estimated against a blended human delivery rate of {formatUsd(data.roi.hourlyRate)}/hour across the SDLC pipeline.
                </p>
              </div>
              <div className="roi-kpis">
                <article className="roi-kpi">
                  <span>Projected time saved</span>
                  <strong>{data.roi.summary.timeSavedHours}h</strong>
                  <small>
                    {data.roi.summary.aiHours}h AI-assisted vs {data.roi.summary.humanHours}h human baseline
                  </small>
                </article>
                <article className="roi-kpi">
                  <span>Projected cost saved</span>
                  <strong>{formatUsd(data.roi.summary.costSaved)}</strong>
                  <small>
                    {formatUsd(data.roi.summary.aiCost)} AI-assisted vs {formatUsd(data.roi.summary.humanCost)} human baseline
                  </small>
                </article>
                <article className="roi-kpi accent">
                  <span>Pipeline acceleration</span>
                  <strong>{data.roi.summary.accelerationPercent}%</strong>
                  <small>Modeled reduction in effort hours over full SDLC execution</small>
                </article>
              </div>
              <div className="roi-realized">
                <strong>Realized to date:</strong> {data.roi.realized.timeSavedHours}h and {formatUsd(data.roi.realized.costSaved)} saved across {data.roi.realized.completedStages}/{data.roi.realized.totalStages} active stages.
              </div>
              <div className="roi-phase-grid" role="table" aria-label="Phase ROI comparison">
                <div className="roi-phase-row roi-phase-head" role="row">
                  <span role="columnheader">Phase</span>
                  <span role="columnheader">Progress</span>
                  <span role="columnheader">Human</span>
                  <span role="columnheader">AI-assisted</span>
                  <span role="columnheader">Saved</span>
                </div>
                {data.roi.phases.map((phase) => (
                  <div className="roi-phase-row" role="row" key={`roi-${phase.phase}`}>
                    <span role="cell">{phase.phase}</span>
                    <span role="cell">{phase.completedStages}/{phase.totalStages}</span>
                    <span role="cell">{phase.humanHours}h</span>
                    <span role="cell">{phase.aiHours}h</span>
                    <span role="cell">{phase.timeSavedHours}h / {formatUsd(phase.costSaved)}</span>
                  </div>
                ))}
              </div>
            </section>

            <div className="hero-side-column">
              <div className="hero-card accent pipeline-state-card">
                <span className="meta-label">Pipeline state</span>
                <strong>{data.project.overallStatus.replace('_', ' ')}</strong>
                <span className="meta-sub">Phase: {data.project.phase}</span>
                {nextAction ? (
                  <>
                    <span className="meta-sub">
                      Next action: Step {nextAction.step} - {nextAction.name}
                    </span>
                    <span className="meta-sub">
                      Next agent: {nextAction.agent} ({nextAction.agentRan ? toneLabel(nextAction.status).toLowerCase() : 'not run'})
                    </span>
                    <span className="meta-sub">Prompt: {nextAction.promptFile.split('/').pop()}</span>
                  </>
                ) : (
                  <span className="meta-sub">All steps complete for this run.</span>
                )}
                {actionQueuePreview.length ? (
                  <div className="state-action-queue">
                    <span className="meta-label">Awaiting action</span>
                    <ul>
                      {actionQueuePreview.map((step) => (
                        <li key={`state-${step.step}`}>
                          Step {step.step} - {step.name} · {step.agent} · {step.agentRan ? toneLabel(step.status) : 'Not run'}
                        </li>
                      ))}
                    </ul>
                    {remainingActionCount > 0 ? (
                      <span className="meta-sub">+{remainingActionCount} more step(s) awaiting action</span>
                    ) : null}
                  </div>
                ) : null}
              </div>

              <article className="hero-card confidence-summary-card">
                <span className="meta-label">Confidence</span>
                <strong>{overallConfidencePercent}% ({overallConfidenceBand})</strong>
                <span className="meta-sub">Pipeline confidence across all stages</span>
                <div className="confidence-summary-grid">
                  <div className="confidence-chip done">
                    <span>High</span>
                    <strong>{highConfidenceStages}</strong>
                  </div>
                  <div className="confidence-chip warn">
                    <span>Medium</span>
                    <strong>{mediumConfidenceStages}</strong>
                  </div>
                  <div className="confidence-chip pend">
                    <span>Low</span>
                    <strong>{lowConfidenceStages}</strong>
                  </div>
                </div>
                {lowestConfidenceStep ? (
                  <span className="meta-sub confidence-detail">
                    Lowest stage confidence: Step {lowestConfidenceStep.step} ({lowestConfidenceStep.confidence.percent}%).
                  </span>
                ) : null}
                <span className="meta-sub confidence-detail">
                  Attention indicators: {confidenceAttentionCount} (eval fails + open governance + low-confidence stages).
                </span>
              </article>
            </div>
          </div>
        </section>
      </section>

      <section className="section-block">
        <div className="section-heading">
          <div>
            <div className="section-kicker">Metrics</div>
            <h2>Progress and control totals</h2>
          </div>
          <div className="section-note">Summary metrics</div>
        </div>
        <section className="stats-grid">
          <article className="metric-card highlight">
            <span className="metric-label">Progress</span>
            <div className="metric-value">{progressPercent}%</div>
            <span className="metric-sub">{data.stats.stepsCompleted} of {data.stats.stepsTotal} stages complete</span>
          </article>
          <article className="metric-card">
            <span className="metric-label">Checkpoints</span>
            <div className="metric-value">{data.stats.checkpointsApproved}/{data.stats.checkpointsRequired}</div>
            <span className="metric-sub">Human approvals recorded in-session</span>
          </article>
          <article className="metric-card">
            <span className="metric-label">Governance</span>
            <div className="metric-value">{data.stats.openGovernanceItems}</div>
            <span className="metric-sub">Open decision or implementation items</span>
          </article>
          <article className="metric-card">
            <span className="metric-label">Eval failures</span>
            <div className={`metric-value ${data.evalSummary.failCount > 0 ? 'danger' : 'success'}`}>{data.stats.openEvalFailures}</div>
            <span className="metric-sub">Artifacts currently failing the deterministic eval</span>
          </article>
        </section>
      </section>

      <details className="collapsible-panel section-block" open>
        <summary className="section-heading collapsible-summary">
          <div>
            <div className="section-kicker">SDLC flow</div>
            <h2>Phases, stages, and checkpoints</h2>
          </div>
          <div className="section-note">Expand to inspect SDLC execution flow</div>
        </summary>
        <SdlcPhaseFlow phases={sdlcPhases} confidenceScoringHelp={confidenceScoringHelp} />
      </details>

      <section className="grid-2">
        <article className="panel">
          <details className="collapsible-panel">
            <summary className="section-heading compact collapsible-summary">
              <div>
                <div className="section-kicker">Guardrails</div>
                <h2>Operational controls in force</h2>
              </div>
              <div className="section-note">Expand to view all guardrails</div>
            </summary>
            <div className="card-list">
              {data.guardrails.map((control) => (
                <div className={`list-card ${toneClass(control.status)}`} key={control.name}>
                  <div className="list-top">
                    <strong>{control.name}</strong>
                    <span className={`pill ${toneClass(control.status)}`}>{shortStatus(control.status)}</span>
                  </div>
                  <div className="list-sub">{control.detail}</div>
                  <div className="list-meta">Source: {control.source}</div>
                </div>
              ))}
            </div>
          </details>
        </article>

        <article className="panel">
          <details className="collapsible-panel">
            <summary className="section-heading compact collapsible-summary">
              <div>
                <div className="section-kicker">Evals</div>
                <h2>Quality and rubric gates</h2>
              </div>
              <div className="section-note">Expand to inspect eval details</div>
            </summary>
            <div className="eval-summary">
            {sortedEvalArtifacts.some((artifact) => evalTone(artifact.gate) === 'err') ? (
              <div className="eval-failure-summary">
                <div className="section-kicker">Failing artifacts</div>
                {sortedEvalArtifacts
                  .filter((artifact) => evalTone(artifact.gate) === 'err')
                  .map((artifact) => (
                    <article className="eval-failure-card" key={artifact.file}>
                      <div className="list-top">
                        <strong>{artifact.file}</strong>
                        <span className="pill err">{artifact.gate}</span>
                      </div>
                      <div className="eval-issue-list">
                        {artifactIssueSummary(artifact).map((issue) => (
                          <span key={`${artifact.file}-${issue}`}>{issue}</span>
                        ))}
                      </div>
                    </article>
                  ))}
              </div>
            ) : null}
            <div className={`eval-banner ${toneClass(data.evalSummary.overallGate === 'PASS' ? 'done' : data.evalSummary.failCount > 0 ? 'err' : 'warn')}`}>
              <div>
                <strong>Overall gate: {data.evalSummary.overallGate}</strong>
                <p>{data.evalSummary.passCount} pass, {data.evalSummary.conditionalCount} conditional, {data.evalSummary.failCount} fail.</p>
                <p className={`eval-scope ${isStepScopedEval ? 'warn' : ''}`}>
                  Evaluation scope: {data.evalSummary.scope} · Strict mode: {data.evalSummary.strictMode}
                </p>
                {isStepScopedEval ? (
                  <p className="eval-scope-callout">
                    Step-scoped runs overwrite `outputs/eval-summary.md` and do not represent full pipeline totals.
                  </p>
                ) : null}
              </div>
            </div>
            <div className={`rubric-banner ${toneClass(rubricSummaryTone(data.evalSummary))}`}>
              <div>
                <strong>Rubric orchestration: {data.evalSummary.rubricAutoEval}</strong>
                <p>
                  {data.evalSummary.rubricCounts.executed} executed, {data.evalSummary.rubricCounts.pass} pass, {data.evalSummary.rubricCounts.conditional} conditional, {data.evalSummary.rubricCounts.fail} fail.
                </p>
                <p className="rubric-mode">
                  Rubric gate mode:{' '}
                  <span className={`pill ${data.evalSummary.rubricGateMode === 'ENFORCED' ? 'err' : data.evalSummary.rubricGateMode === 'ADVISORY' ? 'warn' : 'pend'}`}>
                    {data.evalSummary.rubricGateMode}
                  </span>
                </p>
              </div>
            </div>
            <div className="eval-breakdown">
              <div className="eval-metric done">
                <span>Pass</span>
                <strong>{data.evalSummary.passCount}</strong>
              </div>
              <div className="eval-metric warn">
                <span>Conditional</span>
                <strong>{data.evalSummary.conditionalCount}</strong>
              </div>
              <div className="eval-metric err">
                <span>Fail</span>
                <strong>{data.evalSummary.failCount}</strong>
              </div>
              <div className="eval-metric pend">
                <span>Missing</span>
                <strong>{data.evalSummary.missingCount}</strong>
              </div>
            </div>
            <div className="eval-note">Failing artifacts are listed first with the missing sections and non-zero flags called out directly beneath the gate.</div>
              <div className="table-wrap eval-table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Artifact</th>
                    <th>Quality gate</th>
                    <th>Rubric</th>
                    <th>Why it matters</th>
                  </tr>
                </thead>
                <tbody>
                  {sortedEvalArtifacts.map((artifact) => (
                    <tr key={artifact.file} className={`eval-row ${evalTone(artifact.gate)}`}>
                      <td>
                        <div className="table-title">{artifact.file}</div>
                        <div className="table-sub">Required sections: {artifact.requiredSections.filter((section) => section.present).length}/{artifact.requiredSections.length}</div>
                      </td>
                      <td>
                        <span className={`pill ${evalTone(artifact.gate)}`}>
                          {artifact.gate}
                        </span>
                      </td>
                      <td>
                        <div className="rubric-cell">
                          <span className={`pill ${rubricTone(artifact)}`}>{rubricLabel(artifact)}</span>
                          {artifact.rubric.file ? <div className="table-sub">{artifact.rubric.file}</div> : null}
                        </div>
                      </td>
                      <td>
                        <div className="eval-issue-list">
                          {artifactIssueSummary(artifact).map((issue) => (
                            <span key={`${artifact.file}-${issue}`}>{issue}</span>
                          ))}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            </div>
          </details>
        </article>
      </section>

      <section className="grid-2">
        <article className="panel">
          <details className="collapsible-panel">
            <summary className="section-heading compact collapsible-summary">
              <div>
                <div className="section-kicker">Governance</div>
                <h2>Decision register snapshot</h2>
              </div>
              <div className="section-note">Expand to view full register</div>
            </summary>
            <div className="gov-summary">
              <div className="gov-stat"><strong>{data.governance.openCount}</strong><span>Open</span></div>
              <div className="gov-stat"><strong>{data.governance.resolvedCount}</strong><span>Resolved</span></div>
              <div className="gov-stat"><strong>{data.governance.implementedCount}</strong><span>Implemented</span></div>
            </div>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Item</th>
                    <th>State</th>
                  </tr>
                </thead>
                <tbody>
                  {governanceItems.map((item) => (
                    <tr key={item.id} className={`gov-row ${governanceTone(item.status)}`}>
                      <td>{item.id}</td>
                      <td>
                        <div className="table-title">{item.title}</div>
                        <div className="table-sub">{item.type} · {item.owner} · {item.section}</div>
                      </td>
                      <td>
                        <span className={`pill ${governanceTone(item.status)}`}>{item.status.split(' — ')[0].toLowerCase()}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </details>
        </article>

        <article className="panel">
          <details className="collapsible-panel">
            <summary className="section-heading compact collapsible-summary">
              <div>
                <div className="section-kicker">Blockers</div>
                <h2>What still prevents full completion</h2>
              </div>
              <div className="section-note">Expand to review blockers and task log</div>
            </summary>
            <div className="blocker-list">
              {blockerHighlights.length ? blockerHighlights.map((item) => (
                <div className="blocker-item" key={item}>{item}</div>
              )) : <div className="blocker-item success">No blocker highlights detected.</div>}
            </div>
            <div className="session-card">
              <strong>Task log</strong>
              <p>{data.taskLog.taskTitle}</p>
              <span>{data.taskLog.completedTasks} task entry recorded</span>
            </div>
          </details>
        </article>
      </section>
    </main>
  );
}
