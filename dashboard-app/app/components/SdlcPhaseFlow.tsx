'use client';

import { Fragment, useMemo, useState } from 'react';
import type { DashboardStep } from '../../lib/dashboard-data';

interface CheckpointView {
  id: string;
  label: string;
  passed: boolean;
  source: string;
  interaction: 'HITL' | 'HOTL' | 'HITL + HOTL';
  deviationAt: string;
  deviationDetail: string;
  tone: 'done' | 'warn' | 'pend';
}

interface PhaseView {
  id: string;
  title: string;
  subtitle: string;
  status: 'done' | 'warn' | 'err' | 'pend';
  stages: DashboardStep[];
  checkpoints: CheckpointView[];
  confidence: {
    percent: number;
    band: 'Low' | 'Medium' | 'High';
    drivers: string[];
  };
}

interface Props {
  phases: PhaseView[];
  confidenceScoringHelp: string;
}

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

function toneClass(status: string): 'done' | 'warn' | 'err' | 'pend' {
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

function interactionModes(interaction: string): Array<'HITL' | 'HOTL'> {
  if (interaction === 'HITL + HOTL') {
    return ['HITL', 'HOTL'];
  }

  if (interaction === 'HOTL') {
    return ['HOTL'];
  }

  return ['HITL'];
}

function confidenceBandClass(band: 'Low' | 'Medium' | 'High'): 'err' | 'warn' | 'done' {
  if (band === 'High') {
    return 'done';
  }

  if (band === 'Medium') {
    return 'warn';
  }

  return 'err';
}

function isAttentionCallout(driver: string): boolean {
  return /\(-\d+\)/.test(driver);
}

function parseAttentionDocuments(driver: string): { prefix: string; documents: string[]; suffix: string } | null {
  const marker = 'Document needing attention: ';
  const pluralMarker = 'Document(s) needing attention: ';
  const markerIndex = driver.indexOf(marker);
  const pluralMarkerIndex = driver.indexOf(pluralMarker);

  let activeMarker = marker;
  let index = markerIndex;

  if (pluralMarkerIndex >= 0) {
    activeMarker = pluralMarker;
    index = pluralMarkerIndex;
  }

  if (index < 0) {
    return null;
  }

  const prefix = driver.slice(0, index + activeMarker.length);
  const remainder = driver.slice(index + activeMarker.length);
  const suffix = remainder.endsWith('.') ? '.' : '';
  const documents = (suffix ? remainder.slice(0, -1) : remainder)
    .split(',')
    .map((entry) => entry.trim())
    .filter(Boolean);

  if (!documents.length) {
    return null;
  }

  return { prefix, documents, suffix };
}

function renderDriverText(driver: string, step: DashboardStep) {
  const parsed = parseAttentionDocuments(driver);
  if (!parsed) {
    return driver;
  }

  const linkMap = new Map<string, string>();
  for (const input of step.inputs) {
    linkMap.set(input.path, input.href);
  }
  for (const artifact of step.artifacts) {
    linkMap.set(artifact.path, artifact.href);
  }

  return (
    <>
      {parsed.prefix}
      {parsed.documents.map((documentPath, index) => {
        const href = linkMap.get(documentPath);

        return (
          <Fragment key={`${step.step}-${documentPath}-${index}`}>
            {index > 0 ? ', ' : ''}
            {href ? (
              <a className="confidence-driver-doc-link" href={href} title={documentPath}>
                {documentPath}
              </a>
            ) : (
              documentPath
            )}
          </Fragment>
        );
      })}
      {parsed.suffix}
    </>
  );
}

export default function SdlcPhaseFlow({ phases, confidenceScoringHelp }: Props) {
  const [activePhaseId, setActivePhaseId] = useState<string | null>(phases[0]?.id ?? null);

  const activePhase = useMemo(
    () => phases.find((phase) => phase.id === activePhaseId) ?? null,
    [phases, activePhaseId]
  );

  const togglePhase = (phaseId: string) => {
    setActivePhaseId((current) => (current === phaseId ? null : phaseId));
  };

  return (
    <div className="phase-selector">
      <div className="phase-flow">
        {phases.map((phase, index) => (
          <button
            className={`phase-node ${toneClass(phase.status)} ${activePhaseId === phase.id ? 'active' : ''}`}
            key={phase.id}
            type="button"
            onClick={() => togglePhase(phase.id)}
            aria-expanded={activePhaseId === phase.id}
            aria-controls={`phase-panel-${phase.id}`}
          >
            <div className="phase-top">
              <span className="phase-index">P{index + 1}</span>
              <span className={`pill ${toneClass(phase.status)}`}>{toneLabel(phase.status)}</span>
            </div>
            <h3>{phase.title}</h3>
            <p>{phase.subtitle}</p>
            <div className="phase-meta">{phase.stages.length} stages · {phase.checkpoints.length} checkpoint</div>
            <div className="phase-confidence">
              <span>Confidence</span>
              <span className={`pill ${confidenceBandClass(phase.confidence.band)}`}>
                {phase.confidence.percent}% · {phase.confidence.band}
              </span>
            </div>
          </button>
        ))}
      </div>

      <div className="phase-detail-display">
        {activePhase ? (
          <article className="phase-body phase-detail-panel" id={`phase-panel-${activePhase.id}`}>
            <div className="phase-detail-header">
              <div>
                <div className="section-kicker">{activePhase.title}</div>
                <h3>{activePhase.subtitle}</h3>
              </div>
              <span className={`pill ${toneClass(activePhase.status)}`}>{toneLabel(activePhase.status)}</span>
            </div>

            <div className="phase-stage-grid">
              {activePhase.stages.map((step) => (
                <article className={`phase-stage-card ${toneClass(step.status)}`} key={step.step}>
                  <div className="phase-stage-head">
                    <span className="rail-step">{step.step}</span>
                    <span className={`pill ${toneClass(step.status)}`}>{toneLabel(step.status)}</span>
                  </div>
                  <strong>{step.name}</strong>
                  <div className="confidence-row">
                    <span className="confidence-title">Confidence score</span>
                    <span className="confidence-help" title={confidenceScoringHelp}>How scored</span>
                    <span className={`pill ${confidenceBandClass(step.confidence.band)}`}>
                      {step.confidence.percent}% · {step.confidence.band}
                    </span>
                  </div>
                  <div className="confidence-note">
                    Score is weighted by status, artifact completeness, run evidence, and checkpoint readiness.
                  </div>
                  <div className="confidence-track" role="presentation">
                    <span
                      className={`confidence-fill ${confidenceBandClass(step.confidence.band)}`}
                      style={{ width: `${step.confidence.percent}%` }}
                    />
                  </div>
                  <div className="confidence-driver-list">
                    {step.confidence.drivers.map((driver, index) => (
                      <span
                        className={step.status === 'warn' || step.status === 'err'
                          ? (isAttentionCallout(driver) ? 'needs-attention' : '')
                          : ''}
                        key={`${step.step}-${index}`}
                      >
                        {renderDriverText(driver, step)}
                      </span>
                    ))}
                  </div>
                  <div className="stage-agent-meta">
                    <span><strong>Agent:</strong> {step.agent}</span>
                    <span>
                      <strong>Prompt:</strong>{' '}
                      {step.promptExists ? (
                        <a className="confidence-driver-doc-link" href={step.promptHref} title={step.promptFile}>
                          {step.promptFile.split('/').pop()}
                        </a>
                      ) : (
                        step.promptFile.split('/').pop() ?? step.promptFile
                      )}
                    </span>
                    <span><strong>Agent role:</strong> {step.agentDescription}</span>
                    <span><strong>Persona:</strong> {step.persona}</span>
                    <span><strong>Persona profile:</strong> {step.personaDescription}</span>
                    <span><strong>Model:</strong> {step.modelVersion} ({step.modelVendor})</span>
                    <span><strong>Model captured:</strong> {step.modelCapturedAt}</span>
                    <span><strong>Ran:</strong> {step.agentRan ? 'Yes' : 'No'}</span>
                  </div>
                  <p>{step.note}</p>
                  <div className="artifact-group">
                    <span className="artifact-group-label">Inputs</span>
                    <div className="artifact-link-list">
                      {step.inputs.map((input) => (
                        <a
                          className={`artifact-link ${input.exists ? 'present' : 'missing'}`}
                          key={input.path}
                          href={input.href}
                          title={input.path}
                        >
                          {input.path}
                        </a>
                      ))}
                    </div>
                  </div>
                  <div className="rail-meta">Output: {step.output}</div>
                  <div className="artifact-group">
                    <span className="artifact-group-label">Generated artifacts</span>
                    <div className="artifact-link-list">
                      {step.artifacts.map((artifact) => (
                        <a
                          className={`artifact-link ${artifact.exists ? 'present' : 'missing'}`}
                          key={artifact.path}
                          href={artifact.href}
                          title={artifact.path}
                        >
                          {artifact.path}
                        </a>
                      ))}
                    </div>
                  </div>
                </article>
              ))}
            </div>

            <div className="phase-checkpoint-list">
              {activePhase.checkpoints.map((checkpoint) => (
                <article className={`phase-checkpoint ${toneClass(checkpoint.tone)}`} key={checkpoint.id}>
                  <div className="phase-checkpoint-head">
                    <strong>{checkpoint.id}</strong>
                    <span className={`pill ${toneClass(checkpoint.tone)}`}>{checkpoint.passed ? 'approved' : 'pending approval'}</span>
                  </div>
                  <p>{checkpoint.label}</p>
                  <div className="checkpoint-meta">
                    <span>
                      <strong>Interaction:</strong>{' '}
                      <span className="interaction-chip-group">
                        {interactionModes(checkpoint.interaction).map((mode) => (
                          <span className={`interaction-chip ${mode.toLowerCase()}`} key={`${checkpoint.id}-${mode}`}>
                            {mode}
                          </span>
                        ))}
                      </span>
                    </span>
                    <span><strong>Deviation at:</strong> {checkpoint.deviationAt}</span>
                    <span><strong>Deviation detail:</strong> {checkpoint.deviationDetail}</span>
                  </div>
                  <div className="rail-meta">Evidence: {checkpoint.source}</div>
                </article>
              ))}
            </div>
          </article>
        ) : (
          <div className="phase-collapsed-note">Select a phase to expand details. Click an open phase again to collapse it.</div>
        )}
      </div>
    </div>
  );
}
