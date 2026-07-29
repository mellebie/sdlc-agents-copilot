import fs from 'node:fs';
import path from 'node:path';

export type StatusTone = 'done' | 'warn' | 'err' | 'pend';

export interface DashboardArtifactLink {
  path: string;
  href: string;
  exists: boolean;
}

export interface DashboardStageConfidence {
  percent: number;
  band: 'Low' | 'Medium' | 'High';
  drivers: string[];
}

export interface DashboardStep {
  step: string;
  name: string;
  output: string;
  promptFile: string;
  promptHref: string;
  promptExists: boolean;
  status: StatusTone;
  checkpointRequired: boolean;
  note: string;
  inputs: DashboardArtifactLink[];
  agent: string;
  agentDescription: string;
  persona: string;
  personaDescription: string;
  modelVersion: string;
  modelVendor: string;
  modelCapturedAt: string;
  agentRan: boolean;
  artifacts: DashboardArtifactLink[];
  confidence: DashboardStageConfidence;
}

export interface DashboardCheckpoint {
  label: string;
  passed: boolean;
  source: string;
  interaction: 'HITL' | 'HOTL' | 'HITL + HOTL';
  deviationAt: string;
  deviationDetail: string;
}

export interface EvalArtifact {
  file: string;
  gate: string;
  requiredSections: Array<{ section: string; present: boolean }>;
  flags: Record<string, number>;
  rubric: {
    status: 'EXECUTED' | 'NOT_CONFIGURED' | 'NOT_EXECUTED';
    file: string | null;
    confidencePercent: number | null;
    verdict: 'PASS' | 'CONDITIONAL' | 'FAIL' | null;
  };
}

export interface GovernanceItem {
  id: string;
  title: string;
  type: string;
  owner: string;
  priority: string;
  status: string;
  section: string;
}

export interface GuardrailControl {
  name: string;
  status: StatusTone;
  source: string;
  detail: string;
}

export interface PipelineSnapshot {
  project: {
    name: string;
    client: string;
    phase: string;
    nextStep: string;
    nextPromptFile: string;
    pickup: string;
    branch: string;
    overallStatus: string;
    model: {
      id: string;
      vendor: string;
      displayName: string;
      capturedAt: string;
    };
  };
  stats: {
    stepsCompleted: number;
    stepsTotal: number;
    checkpointsApproved: number;
    checkpointsRequired: number;
    openGovernanceItems: number;
    openEvalFailures: number;
  };
  steps: DashboardStep[];
  checkpoints: DashboardCheckpoint[];
  evalSummary: {
    overallGate: string;
    scope: string;
    strictMode: string;
    rubricAutoEval: string;
    rubricGateMode: 'ENFORCED' | 'ADVISORY' | 'UNKNOWN';
    passCount: number;
    conditionalCount: number;
    failCount: number;
    missingCount: number;
    rubricCounts: {
      executed: number;
      pass: number;
      conditional: number;
      fail: number;
    };
    artifacts: EvalArtifact[];
  };
  governance: {
    openCount: number;
    resolvedCount: number;
    implementedCount: number;
    items: GovernanceItem[];
  };
  guardrails: GuardrailControl[];
  taskLog: {
    completedTasks: number;
    totalTasks: number;
    taskTitle: string;
    limitations: string[];
  };
  roi: {
    hourlyRate: number;
    summary: {
      humanHours: number;
      aiHours: number;
      timeSavedHours: number;
      accelerationPercent: number;
      humanCost: number;
      aiCost: number;
      costSaved: number;
    };
    realized: {
      completedStages: number;
      totalStages: number;
      humanHours: number;
      aiHours: number;
      timeSavedHours: number;
      accelerationPercent: number;
      humanCost: number;
      aiCost: number;
      costSaved: number;
    };
    phases: Array<{
      phase: string;
      completedStages: number;
      totalStages: number;
      humanHours: number;
      aiHours: number;
      timeSavedHours: number;
      humanCost: number;
      aiCost: number;
      costSaved: number;
    }>;
  };
}

interface ManifestStep {
  step: string;
  name: string;
  prompt_file?: string;
  output_file: string;
  model_version?: string;
  checkpoint_required?: boolean;
}

interface Manifest {
  run?: {
    branch?: string;
    model?: {
      id?: string;
      vendor?: string;
      display_name?: string;
      captured_at?: string;
    };
  };
  summary?: {
    overall_status?: string;
  };
  steps?: ManifestStep[];
}

const repoRoot = findRepoRoot();

function findRepoRoot(startDir: string = process.cwd()): string {
  let current = startDir;
  while (true) {
    if (fs.existsSync(path.join(current, 'outputs')) && fs.existsSync(path.join(current, 'decisions'))) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      return startDir;
    }

    current = parent;
  }
}

function readText(relativePath: string): string {
  const absolutePath = path.join(repoRoot, relativePath);
  return fs.existsSync(absolutePath) ? fs.readFileSync(absolutePath, 'utf8') : '';
}

function readJson(relativePath: string): Manifest {
  const content = readText(relativePath);
  if (!content) {
    return {};
  }

  try {
    return JSON.parse(content) as Manifest;
  } catch {
    return {};
  }
}

function exists(relativePath: string): boolean {
  return fs.existsSync(path.join(repoRoot, relativePath));
}

function artifactExists(relativePath: string): boolean {
  const absolutePath = path.join(repoRoot, relativePath);
  if (!fs.existsSync(absolutePath)) {
    return false;
  }

  const stat = fs.statSync(absolutePath);
  if (stat.isFile()) {
    return true;
  }

  if (!stat.isDirectory()) {
    return false;
  }

  const entries = fs.readdirSync(absolutePath, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.name === '.gitkeep') {
      continue;
    }

    if (entry.isFile()) {
      return true;
    }

    if (entry.isDirectory() && artifactExists(path.join(relativePath, entry.name))) {
      return true;
    }
  }

  return false;
}

function toVscodeHref(relativePath: string): string {
  const absolutePath = path.join(repoRoot, relativePath).replace(/\\/g, '/');
  const encoded = absolutePath
    .split('/')
    .map((segment) => encodeURIComponent(segment))
    .join('/');
  return `vscode://file/${encoded}`;
}

function bandFromPercent(percent: number): 'Low' | 'Medium' | 'High' {
  if (percent >= 75) {
    return 'High';
  }

  if (percent >= 40) {
    return 'Medium';
  }

  return 'Low';
}

function computeStageConfidence(
  status: StatusTone,
  outputPath: string,
  inputs: DashboardArtifactLink[],
  artifacts: DashboardArtifactLink[],
  agentRan: boolean,
  checkpointRequired: boolean
): DashboardStageConfidence {
  const penalty = {
    warnStatus: 20,
    errStatus: 55,
    pendingStatus: 70,
    missingInput: 16,
    missingOutput: 12,
    noAgentRunEvidence: 20,
    checkpointNotDone: 8
  };

  let score = 100;
  const drivers: string[] = [];

  if (status === 'done') {
    drivers.push('Stage output meets current strict gate criteria.');
  } else if (status === 'warn') {
    score -= penalty.warnStatus;
    drivers.push(`Stage is draft/conditional and not yet fully approved (-${penalty.warnStatus}). Document needing attention: ${outputPath}.`);
  } else if (status === 'err') {
    score -= penalty.errStatus;
    drivers.push(`Blocking verdict detected in stage artifact (-${penalty.errStatus}). Document needing attention: ${outputPath}.`);
  } else {
    score -= penalty.pendingStatus;
    drivers.push(`Stage has not run or expected artifact is missing (-${penalty.pendingStatus}). Document needing attention: ${outputPath}.`);
  }

  const missingInputs = inputs.filter((input) => !input.exists);
  const missingArtifacts = artifacts.filter((artifact) => !artifact.exists);

  if (missingInputs.length > 0) {
    const applied = missingInputs.length * penalty.missingInput;
    score -= applied;
    drivers.push(`${missingInputs.length} required input artifact(s) missing (-${applied}). Document(s) needing attention: ${missingInputs.map((input) => input.path).join(', ')}.`);
  } else {
    drivers.push('All mapped input artifacts are present.');
  }

  if (missingArtifacts.length > 0) {
    const applied = missingArtifacts.length * penalty.missingOutput;
    score -= applied;
    drivers.push(`${missingArtifacts.length} generated artifact(s) missing (-${applied}). Document(s) needing attention: ${missingArtifacts.map((artifact) => artifact.path).join(', ')}.`);
  } else {
    drivers.push('All mapped output artifacts are present.');
  }

  if (!agentRan) {
    score -= penalty.noAgentRunEvidence;
    drivers.push(`Agent run evidence not detected for this stage (-${penalty.noAgentRunEvidence}). Document needing attention: ${outputPath}.`);
  } else {
    drivers.push('Agent run evidence detected from stage outputs.');
  }

  if (checkpointRequired && status !== 'done') {
    score -= penalty.checkpointNotDone;
    drivers.push(`Checkpoint-linked stage is not yet at done status (-${penalty.checkpointNotDone}). Document needing attention: ${outputPath}.`);
  }

  const clamped = Math.max(0, Math.min(100, score));
  return {
    percent: clamped,
    band: bandFromPercent(clamped),
    drivers
  };
}

function directoryHasFiles(relativePath: string, predicate?: (fileName: string) => boolean): boolean {
  const absolutePath = path.join(repoRoot, relativePath);
  if (!fs.existsSync(absolutePath)) {
    return false;
  }

  const entries = fs.readdirSync(absolutePath, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.name === '.gitkeep') {
      continue;
    }

    const entryPath = path.join(absolutePath, entry.name);
    if (entry.isFile()) {
      if (!predicate || predicate(entry.name)) {
        return true;
      }
      continue;
    }

    if (entry.isDirectory() && directoryHasFiles(path.join(relativePath, entry.name), predicate)) {
      return true;
    }
  }

  return false;
}

function findBrdWordDocument(): string | null {
  const preferred = ['inputs/brd.docx', 'inputs/brd.doc'];
  for (const candidate of preferred) {
    if (artifactExists(candidate)) {
      return candidate;
    }
  }

  const inputDir = path.join(repoRoot, 'inputs');
  if (!fs.existsSync(inputDir)) {
    return null;
  }

  const entries = fs.readdirSync(inputDir, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isFile()) {
      continue;
    }

    const lowerName = entry.name.toLowerCase();
    if (lowerName.endsWith('.docx') || lowerName.endsWith('.doc')) {
      return `inputs/${entry.name}`;
    }
  }

  return null;
}

function deriveBrdBootstrapStatus(): StatusTone {
  if (artifactExists('inputs/brd.md')) {
    return 'done';
  }

  if (findBrdWordDocument()) {
    return 'warn';
  }

  return 'pend';
}

function countMarkdownMatches(content: string, pattern: RegExp): number {
  if (!content) {
    return 0;
  }

  const matches = content.match(pattern);
  return matches ? matches.length : 0;
}

function parseEvalSummary(): PipelineSnapshot['evalSummary'] {
  const content = readText('outputs/eval-summary.md');
  const normalized = content.replace(/\r\n/g, '\n');
  const readSummaryField = (label: string, fallback: string = 'UNKNOWN'): string => {
    const escaped = label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const standardPattern = new RegExp(`\\*\\*${escaped}:\\*\\*\\s*([^\\n]+)`, 'i');
    const legacyPattern = new RegExp(`\\*\\*${escaped}:\\s*([^\\n]+?)\\*\\*`, 'i');
    return (normalized.match(standardPattern) ?? normalized.match(legacyPattern) ?? [])[1]?.trim() ?? fallback;
  };
  const passCount = Number((normalized.match(/\|\s*PASS\s*\|\s*(\d+)\s*\|/i) ?? [])[1] ?? 0);
  const conditionalCount = Number((normalized.match(/\|\s*CONDITIONAL\s*\|\s*(\d+)\s*\|/i) ?? [])[1] ?? 0);
  const failCount = Number((normalized.match(/\|\s*FAIL\s*\|\s*(\d+)\s*\|/i) ?? [])[1] ?? 0);
  const missingCount = Number((normalized.match(/\|\s*MISSING\s*\|\s*(\d+)\s*\|/i) ?? [])[1] ?? 0);
  const overallGate = (normalized.match(/\*\*Overall pipeline gate:\s*([A-Z]+)\*\*/i) ?? [])[1] ?? 'UNKNOWN';
  const scope = readSummaryField('Evaluation scope', 'Unknown');
  const strictMode = readSummaryField('Post-Checkpoint-3 strict mode');
  let rubricAutoEval = readSummaryField('Rubric auto-eval');
  const rubricGateEnforcementRaw = readSummaryField('Rubric gate enforcement', '');
  let rubricGateMode: PipelineSnapshot['evalSummary']['rubricGateMode'] = 'UNKNOWN';
  if (/enabled|enforced/i.test(rubricGateEnforcementRaw)) {
    rubricGateMode = 'ENFORCED';
  } else if (/disabled|advisory/i.test(rubricGateEnforcementRaw)) {
    rubricGateMode = 'ADVISORY';
  } else if (/enabled/i.test(rubricAutoEval)) {
    // Current summary format may not emit enforcement line; default behavior is advisory unless explicitly enforced.
    rubricGateMode = 'ADVISORY';
  }

  const rubricSummaryBlock = normalized.match(/\|\s*Rubric Result\s*\|\s*Count\s*\|\n\|\s*---\s*\|\s*---\s*\|\n([\s\S]*?)\n\n\*\*Overall pipeline gate:/i);
  const rubricCounts = {
    executed: 0,
    pass: 0,
    conditional: 0,
    fail: 0
  };
  if (rubricSummaryBlock) {
    for (const row of rubricSummaryBlock[1].trim().split('\n')) {
      const cells = row.match(/^\|\s*([^|]+?)\s*\|\s*(\d+)\s*\|$/);
      if (!cells) {
        continue;
      }

      const key = cells[1].trim().toUpperCase();
      const count = Number(cells[2]);
      if (key === 'EXECUTED') {
        rubricCounts.executed = count;
      } else if (key === 'PASS') {
        rubricCounts.pass = count;
      } else if (key === 'CONDITIONAL') {
        rubricCounts.conditional = count;
      } else if (key === 'FAIL') {
        rubricCounts.fail = count;
      }
    }
  }

  // Backfill rubric status when older summaries omit explicit header lines.
  if (rubricAutoEval.toUpperCase() === 'UNKNOWN' && rubricCounts.executed > 0) {
    rubricAutoEval = 'ENABLED';
  }

  const artifacts: EvalArtifact[] = [];
  const artifactBlockRegex = /^##\s+([^\n]+)\n\n\*\*Quality gate:\*\*\s*([^\n]+)\n([\s\S]*?)(?=^---\s*$|^##\s+Summary\s*$|\Z)/gmi;
  for (const match of normalized.matchAll(artifactBlockRegex)) {
    const [, file, gate, body] = match;
    const requiredSections: Array<{ section: string; present: boolean }> = [];
    const sectionBlock = body.match(/###\s+Required Sections\n\n\|\s*Section\s*\|\s*Present\s*\|\n\|\s*---\s*\|\s*---\s*\|\n([\s\S]*?)\n\n###\s+Flag Counts/i);
    if (sectionBlock) {
      for (const row of sectionBlock[1].trim().split('\n')) {
        const cellMatch = row.match(/^\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|$/);
        if (cellMatch) {
          const presentCell = cellMatch[2].trim();
          const isPresent = presentCell.includes('✅')
            || presentCell.includes('âœ…')
            || /^\s*(yes|true|pass)\s*$/i.test(presentCell);

          requiredSections.push({
            section: cellMatch[1].trim(),
            present: isPresent
          });
        }
      }
    }

    const flags: Record<string, number> = {};
    for (const flagName of ['AMBIGUOUS', 'GAP', 'ASSUMPTION', 'BLOCKER', 'TCPA_RISK', 'NEW_DEPENDENCY', 'CRITICAL', 'SECURITY_BLOCK', 'BLOCKING']) {
      const regex = new RegExp(`\\|\\s*${flagName}.*?\\|\\s*(\\d+)\\s*\\|`, 'i');
      const flagMatch = body.match(regex);
      flags[flagName] = Number(flagMatch?.[1] ?? 0);
    }

    const rubricSection = body.match(/###\s+Rubric Evaluation\n\n([\s\S]*?)$/i)?.[1] ?? '';
    const noRubricConfigured = /No rubric file matched this step/i.test(rubricSection);
    const rubricFile = (rubricSection.match(/\*\*Rubric file:\*\*\s*([^\n]+)/i) ?? [])[1]?.trim() ?? null;
    const rubricConfidenceRaw = (rubricSection.match(/\*\*Rubric confidence:\*\*\s*(\d+)%/i) ?? [])[1];
    const rubricConfidencePercent = rubricConfidenceRaw ? Number(rubricConfidenceRaw) : null;
    const rubricVerdictRaw = (rubricSection.match(/\*\*Rubric verdict:\*\*\s*([^\n]+)/i) ?? [])[1]?.trim().toUpperCase() ?? null;
    const rubricVerdict = rubricVerdictRaw === 'PASS' || rubricVerdictRaw === 'CONDITIONAL' || rubricVerdictRaw === 'FAIL'
      ? rubricVerdictRaw
      : null;

    let rubricStatus: EvalArtifact['rubric']['status'] = 'NOT_EXECUTED';
    if (noRubricConfigured) {
      rubricStatus = 'NOT_CONFIGURED';
    } else if (rubricFile || rubricVerdict || rubricConfidencePercent !== null) {
      rubricStatus = 'EXECUTED';
    }

    artifacts.push({
      file,
      gate: gate.trim(),
      requiredSections,
      flags,
      rubric: {
        status: rubricStatus,
        file: rubricFile,
        confidencePercent: rubricConfidencePercent,
        verdict: rubricVerdict
      }
    });
  }

  return {
    overallGate,
    scope,
    strictMode,
    rubricAutoEval,
    rubricGateMode,
    passCount,
    conditionalCount,
    failCount,
    missingCount,
    rubricCounts,
    artifacts
  };
}

function parseGovernance(): PipelineSnapshot['governance'] {
  const content = readText('decisions/guardrails-evals-governance.md');
  const blocks = content.split(/^###\s+/m).slice(1);
  const items: GovernanceItem[] = [];

  for (const block of blocks) {
    const heading = (block.match(/^(.+)$/m) ?? [])[1]?.trim() ?? '';
    const id = (heading.match(/^([A-Z]+-[A-Z]+-\d+)/) ?? [])[1] ?? 'UNKNOWN';
    const title = heading.replace(/^([A-Z]+-[A-Z]+-\d+)\s*·\s*(DECISION|IMPLEMENTATION)\s*·\s*/, '').trim();
    const type = (block.match(/\*\*Type:\*\*\s*([^\n]+)/) ?? [])[1]?.trim() ?? 'Unknown';
    const owner = (block.match(/\*\*Owner:\*\*\s*([^\n]+)/) ?? [])[1]?.trim() ?? 'Unknown';
    const priority = (block.match(/\*\*Priority:\*\*\s*([^\n]+)/) ?? [])[1]?.trim() ?? 'Unknown';
    const status = (block.match(/\*\*Status:\*\*\s*([^\n]+)/) ?? [])[1]?.trim() ?? 'UNKNOWN';
    const section = (content.slice(0, content.indexOf('### ' + heading)).match(/## SECTION\s+\d+\s+—\s+([\s\S]*?)$/m) ?? [])[1]?.trim() ?? 'Governance';

    items.push({
      id,
      title,
      type,
      owner,
      priority,
      status,
      section
    });
  }

  const openCount = items.filter((item) => item.status.startsWith('OPEN')).length;
  const resolvedCount = items.filter((item) => item.status.startsWith('RESOLVED')).length;
  const implementedCount = items.filter((item) => item.status.startsWith('IMPLEMENTED')).length;

  return {
    openCount,
    resolvedCount,
    implementedCount,
    items
  };
}

function parseTaskLog(): PipelineSnapshot['taskLog'] {
  const content = readText('outputs/task-log.md');
  const title = (content.match(/^##\s+TASK-\d+:\s+(.+)$/m) ?? [])[1]?.trim() ?? 'No task log entries yet';
  const completedTasks = countMarkdownMatches(content, /^##\s+TASK-\d+:/gm);
  const totalTasks = completedTasks;
  const limitations = [] as string[];

  const limitationRegex = /^-\s+\*\*Known Limitations:\*\*\s*$/m;
  if (limitationRegex.test(content)) {
    const blockMatch = content.match(/\*\*Known Limitations:\*\*\n((?:\s*- .+\n?)+)/m);
    if (blockMatch) {
      for (const line of blockMatch[1].trim().split('\n')) {
        limitations.push(line.replace(/^\s*-\s*/, '').trim());
      }
    }
  }

  return {
    completedTasks,
    totalTasks,
    taskTitle: title,
    limitations
  };
}

function deriveArtifactStatus(relativePath: string, mode: 'file' | 'directory' = 'file'): StatusTone {
  if (mode === 'directory') {
    return directoryHasFiles(relativePath, (fileName) => fileName.endsWith('.cs') || fileName.endsWith('.md')) ? 'done' : 'pend';
  }

  if (!exists(relativePath)) {
    return 'pend';
  }

  // Input artifacts are considered complete based on presence.
  if (relativePath.startsWith('inputs/')) {
    return 'done';
  }

  const content = readText(relativePath);
  if (/Overall Verdict:\s*CHANGES REQUIRED/i.test(content)) {
    return 'err';
  }

  if (/Overall Security Verdict:\s*FAIL/i.test(content)) {
    return 'err';
  }

  if (/Overall Verdict:\s*APPROVED WITH CONDITIONS/i.test(content)) {
    return 'warn';
  }

  if (/Overall Security Verdict:\s*PASS WITH CONDITIONS/i.test(content)) {
    return 'warn';
  }

  if (/Status:\s*(APPROVED WITH CONDITIONS|PASS WITH CONDITIONS)/i.test(content)) {
    return 'warn';
  }

  if (/Status:\s*DRAFT/i.test(content)) {
    return 'warn';
  }

  return 'done';
}

function buildPipelineSteps(): DashboardStep[] {
  const manifest = readJson('outputs/pipeline-manifest.json');
  const manifestSteps = manifest.steps ?? [];

  const stepMap = new Map(manifestSteps.map((step) => [step.step, step]));
  const steps: DashboardStep[] = [];

  const stepDefinitions: Array<{
    step: string;
    output: string;
    promptFile: string;
    inputs: string[];
    artifacts: string[];
    mode: 'file' | 'directory';
    note: string;
    agent: string;
    agentDescription: string;
    persona: string;
    personaDescription: string;
  }> = [
    {
      step: '0',
      output: 'inputs/brd.md',
      promptFile: '.github/prompts/00a-brd-bootstrap.prompt.md',
      inputs: ['inputs/brd.docx', 'scripts/Convert-BrdDocToMarkdown.ps1'],
      artifacts: ['inputs/brd.md'],
      mode: 'file',
      note: 'BRD markdown bootstrap ready.',
      agent: 'BRD Markdown Bootstrap',
      agentDescription: 'Converts Word BRD input into markdown before pipeline execution when required.',
      persona: 'Avery - Intake Engineer',
      personaDescription: 'Prepares source intake artifacts so downstream pipeline stages can start cleanly.'
    },
    {
      step: '00',
      output: 'inputs/prd.md',
      promptFile: '.github/prompts/00-brd-to-prd.prompt.md',
      inputs: ['inputs/brd.md'],
      artifacts: ['inputs/prd.md'],
      mode: 'file',
      note: 'PRD source loaded.',
      agent: 'BRD to PRD Bridge',
      agentDescription: 'Converts business requirements into a structured PRD artifact.',
      persona: 'Alex - The Translator',
      personaDescription: 'Turns stakeholder language into implementation-ready product requirements.'
    },
    {
      step: '01',
      output: 'outputs/requirements.md',
      promptFile: '.github/prompts/01-prd-analyst.prompt.md',
      inputs: ['inputs/prd.md'],
      artifacts: ['outputs/requirements.md'],
      mode: 'file',
      note: 'Requirements artifact present.',
      agent: 'PRD Analyst',
      agentDescription: 'Extracts functional and non-functional requirements with traceability.',
      persona: 'Sam - The Forensic Analyst',
      personaDescription: 'Interrogates PRD details and records explicit acceptance criteria and constraints.'
    },
    {
      step: '02',
      output: 'outputs/clarifications.md',
      promptFile: '.github/prompts/02-clarification.prompt.md',
      inputs: ['inputs/prd.md', 'outputs/requirements.md'],
      artifacts: ['outputs/clarifications.md'],
      mode: 'file',
      note: 'Clarifications artifact present.',
      agent: 'Clarification',
      agentDescription: 'Captures ambiguity and blocking questions that need human resolution.',
      persona: 'Jordan - The Interrogator',
      personaDescription: 'Drives targeted questions to close scope gaps and conflicting assumptions.'
    },
    {
      step: '03',
      output: 'outputs/specs.md',
      promptFile: '.github/prompts/03-spec-decomposer.prompt.md',
      inputs: ['outputs/requirements.md', 'outputs/clarifications.md'],
      artifacts: ['outputs/specs.md'],
      mode: 'file',
      note: 'Specification artifact present.',
      agent: 'Spec Decomposer',
      agentDescription: 'Breaks requirements into precise, implementable feature specifications.',
      persona: 'Taylor - The Precision Engineer',
      personaDescription: 'Defines bounded, testable specs with explicit edge-case handling.'
    },
    {
      step: '04',
      output: 'outputs/architecture.md',
      promptFile: '.github/prompts/04-architecture.prompt.md',
      inputs: ['outputs/specs.md'],
      artifacts: ['outputs/architecture.md'],
      mode: 'file',
      note: 'Architecture artifact present.',
      agent: 'Architecture',
      agentDescription: 'Designs the system architecture and technical decomposition strategy.',
      persona: 'Winston - The Architect',
      personaDescription: 'Shapes component boundaries, contracts, and operational architecture decisions.'
    },
    {
      step: '05',
      output: 'outputs/risks.md',
      promptFile: '.github/prompts/05-risk-assessment.prompt.md',
      inputs: ['outputs/specs.md', 'outputs/architecture.md'],
      artifacts: ['outputs/risks.md'],
      mode: 'file',
      note: 'Risk register present.',
      agent: 'Risk Assessment',
      agentDescription: 'Builds the delivery risk register with mitigation recommendations.',
      persona: 'Morgan - The Risk Officer',
      personaDescription: 'Assesses severity, likelihood, and mitigation actions across technical and delivery risks.'
    },
    {
      step: '06',
      output: 'outputs/stories.md',
      promptFile: '.github/prompts/06-story-writer.prompt.md',
      inputs: ['outputs/specs.md', 'outputs/architecture.md', 'outputs/risks.md'],
      artifacts: ['outputs/stories.md'],
      mode: 'file',
      note: 'Story backlog present.',
      agent: 'Story Writer',
      agentDescription: 'Converts approved design intent into user stories and acceptance criteria.',
      persona: 'Riley - The Product Owner',
      personaDescription: 'Frames user value and aligns story wording to business outcomes.'
    },
    {
      step: '07',
      output: 'outputs/tasks.md',
      promptFile: '.github/prompts/07-task-breakdown.prompt.md',
      inputs: ['outputs/stories.md'],
      artifacts: ['outputs/tasks.md'],
      mode: 'file',
      note: 'Task board present.',
      agent: 'Task Breakdown',
      agentDescription: 'Decomposes stories into executable implementation tasks and dependencies.',
      persona: 'Casey - The Tech Lead',
      personaDescription: 'Organizes sequencing, effort, and technical ownership for build execution.'
    },
    {
      step: '08',
      output: 'src',
      promptFile: '.github/prompts/08-code-generator.prompt.md',
      inputs: ['outputs/specs.md', 'outputs/architecture.md', 'outputs/tasks.md'],
      artifacts: ['src', 'outputs/task-log.md'],
      mode: 'directory',
      note: 'Implementation code is present.',
      agent: 'Code Generator',
      agentDescription: 'Implements staged code increments and logs execution details.',
      persona: 'Amelia - The Engineer',
      personaDescription: 'Builds production code slices while tracking deviations and limitations.'
    },
    {
      step: '09',
      output: 'tests',
      promptFile: '.github/prompts/09-test-generator.prompt.md',
      inputs: ['outputs/stories.md', 'outputs/tasks.md', 'src'],
      artifacts: ['tests', 'outputs/task-log.md'],
      mode: 'directory',
      note: 'No committed .cs test files yet.',
      agent: 'Test Generator',
      agentDescription: 'Generates unit, integration, and functional validation assets.',
      persona: 'Quinn - The QA Engineer',
      personaDescription: 'Builds coverage-focused tests to validate acceptance criteria and edge cases.'
    },
    {
      step: '10',
      output: 'outputs/review-findings.md',
      promptFile: '.github/prompts/10-code-reviewer.prompt.md',
      inputs: ['src', 'tests', 'outputs/task-log.md'],
      artifacts: ['outputs/review-findings.md'],
      mode: 'file',
      note: 'Code review not run yet.',
      agent: 'Code Reviewer',
      agentDescription: 'Performs structured code quality review and records findings.',
      persona: 'Blake - The Principal Engineer',
      personaDescription: 'Evaluates reliability, maintainability, and correctness before release.'
    },
    {
      step: '11',
      output: 'outputs/security-findings.md',
      promptFile: '.github/prompts/11-security-agent.prompt.md',
      inputs: ['src', 'tests', 'outputs/task-log.md'],
      artifacts: ['outputs/security-findings.md'],
      mode: 'file',
      note: 'Security review not run yet.',
      agent: 'Security Agent',
      agentDescription: 'Runs security posture analysis and documents risk findings.',
      persona: 'Robin - The Security Engineer',
      personaDescription: 'Focuses on vulnerabilities, hardening, and exploit-prevention coverage.'
    },
    {
      step: '12',
      output: 'outputs/docs',
      promptFile: '.github/prompts/12-documentation.prompt.md',
      inputs: ['outputs/review-findings.md', 'outputs/security-findings.md', 'outputs/task-log.md'],
      artifacts: ['outputs/docs'],
      mode: 'directory',
      note: 'Documentation set not started yet.',
      agent: 'Documentation',
      agentDescription: 'Produces release-facing technical and operational documentation.',
      persona: 'Jamie - The Tech Writer',
      personaDescription: 'Converts build outcomes and controls into clear delivery documentation.'
    },
    {
      step: '13',
      output: 'outputs/pr-description.md',
      promptFile: '.github/prompts/13-pr-assembler.prompt.md',
      inputs: ['outputs/docs', 'outputs/review-findings.md', 'outputs/security-findings.md'],
      artifacts: ['outputs/pr-description.md'],
      mode: 'file',
      note: 'PR assembly not started yet.',
      agent: 'PR Assembler',
      agentDescription: 'Assembles final PR packaging and release handoff narrative.',
      persona: 'Sage - The Delivery Lead',
      personaDescription: 'Finalizes delivery context, approvals, and release-readiness communication.'
    }
  ];

  for (const definition of stepDefinitions) {
    const manifestStep = stepMap.get(definition.step);
    const wordSourcePath = definition.step === '0' ? findBrdWordDocument() : null;
    const status = definition.step === '0'
      ? deriveBrdBootstrapStatus()
      : deriveArtifactStatus(definition.output, definition.mode);
    const label = manifestStep?.name ?? `Step ${definition.step}`;
    const promptFile = manifestStep?.prompt_file ?? definition.promptFile;
    const artifacts = definition.artifacts.map((artifactPath) => ({
      path: artifactPath,
      href: toVscodeHref(artifactPath),
      exists: artifactExists(artifactPath)
    }));
    const inputPaths = definition.step === '0'
      ? [wordSourcePath ?? 'inputs/brd.docx', 'scripts/Convert-BrdDocToMarkdown.ps1']
      : definition.inputs;
    const inputs = inputPaths.map((inputPath) => ({
      path: inputPath,
      href: toVscodeHref(inputPath),
      exists: artifactExists(inputPath)
    }));
    const agentRan = definition.step === '0'
      ? artifactExists('inputs/brd.md')
      : (status !== 'pend' || artifacts.some((artifact) => artifact.exists));
    const note = definition.step === '0'
      ? (status === 'done'
        ? 'BRD markdown source is present in inputs/brd.md.'
        : status === 'warn'
          ? `Word BRD detected at ${wordSourcePath}; convert it using scripts/Convert-BrdDocToMarkdown.ps1.`
          : 'No BRD markdown present yet. Add a Word BRD to inputs/ and run the conversion bootstrap script.')
      : definition.note;
    const confidence = computeStageConfidence(
      status,
      definition.output,
      inputs,
      artifacts,
      agentRan,
      Boolean(manifestStep?.checkpoint_required)
    );

    steps.push({
      step: definition.step,
      name: label,
      output: definition.output,
      promptFile,
      promptHref: toVscodeHref(promptFile),
      promptExists: artifactExists(promptFile),
      status,
      checkpointRequired: Boolean(manifestStep?.checkpoint_required),
      note,
      inputs,
      agent: definition.agent,
      agentDescription: definition.agentDescription,
      persona: definition.persona,
      personaDescription: definition.personaDescription,
      modelVersion: manifestStep?.model_version ?? manifest.run?.model?.id ?? 'unknown',
      modelVendor: manifest.run?.model?.vendor ?? 'unknown',
      modelCapturedAt: manifest.run?.model?.captured_at ?? 'unknown',
      agentRan,
      artifacts,
      confidence
    });
  }

  return steps;
}

function buildCheckpoints(steps: DashboardStep[]): DashboardCheckpoint[] {
  const stepLookup = new Map(steps.map((step) => [step.step, step]));

  const checkpointDefinitions: Array<{
    label: string;
    source: string;
    interaction: 'HITL' | 'HOTL' | 'HITL + HOTL';
    stageIds: string[];
  }> = [
    {
      label: 'Checkpoint 0 - PRD review approved',
      source: 'inputs/prd.md',
      interaction: 'HITL',
      stageIds: ['00']
    },
    {
      label: 'Checkpoint 1 - requirements sign-off',
      source: 'outputs/clarifications.md',
      interaction: 'HITL',
      stageIds: ['01', '02']
    },
    {
      label: 'Checkpoint 2 - architecture & risk approved',
      source: 'outputs/risks.md',
      interaction: 'HITL + HOTL',
      stageIds: ['04', '05']
    },
    {
      label: 'Checkpoint 3 - stories & tasks approved',
      source: 'outputs/tasks.md',
      interaction: 'HITL + HOTL',
      stageIds: ['06', '07']
    },
    {
      label: 'Checkpoint 4 - review & security sign-off',
      source: 'outputs/security-findings.md',
      interaction: 'HITL + HOTL',
      stageIds: ['10', '11']
    }
  ];

  const getDeviation = (stageIds: string[]): { deviationAt: string; deviationDetail: string } => {
    for (const stageId of stageIds) {
      const stage = stepLookup.get(stageId);
      if (!stage) {
        return {
          deviationAt: `Stage ${stageId}`,
          deviationDetail: 'Stage metadata unavailable in current snapshot.'
        };
      }

      if (stage.status === 'warn') {
        return {
          deviationAt: `Stage ${stageId} (${stage.name})`,
          deviationDetail: 'Artifact exists but is still in DRAFT or conditional state.'
        };
      }

      if (stage.status === 'err') {
        return {
          deviationAt: `Stage ${stageId} (${stage.name})`,
          deviationDetail: 'Blocking verdict detected in the stage output.'
        };
      }

      if (stage.status === 'pend') {
        return {
          deviationAt: `Stage ${stageId} (${stage.name})`,
          deviationDetail: 'Expected artifact is missing or stage has not run yet.'
        };
      }
    }

    return {
      deviationAt: 'None',
      deviationDetail: 'No deviation detected; checkpoint criteria currently satisfied.'
    };
  };

  const cp0 = stepLookup.get('00')?.status === 'done';
  const cp1 = ['01', '02'].every((key) => stepLookup.get(key)?.status === 'done');
  const cp2 = ['04', '05'].every((key) => stepLookup.get(key)?.status === 'done');
  const cp3 = ['06', '07'].every((key) => stepLookup.get(key)?.status === 'done');
  const cp4 = ['10', '11'].every((key) => stepLookup.get(key)?.status === 'done');
  const passVector = [cp0, cp1, cp2, cp3, cp4];

  return checkpointDefinitions.map((checkpoint, index) => {
    const passed = passVector[index] ?? false;
    const deviation = passed
      ? {
          deviationAt: 'None',
          deviationDetail: 'No deviation detected; checkpoint criteria currently satisfied.'
        }
      : getDeviation(checkpoint.stageIds);

    return {
      label: checkpoint.label,
      passed,
      source: checkpoint.source,
      interaction: checkpoint.interaction,
      deviationAt: deviation.deviationAt,
      deviationDetail: deviation.deviationDetail
    };
  });
}

function buildGuardrails(): GuardrailControl[] {
  return [
    {
      name: 'Pipeline input validation',
      status: exists('scripts/Validate-PipelineInputs.ps1') ? 'done' : 'pend',
      source: 'scripts/Validate-PipelineInputs.ps1',
      detail: 'Blocks common PII patterns and forbidden file types before intake.'
    },
    {
      name: 'Deterministic eval summary',
      status: exists('scripts/Invoke-PipelineEval.ps1') && exists('outputs/eval-summary.md') ? 'done' : 'pend',
      source: 'scripts/Invoke-PipelineEval.ps1',
      detail: 'Produces a stable eval summary from current output artifacts.'
    },
    {
      name: 'Pipeline manifest writer',
      status: exists('scripts/Write-PipelineManifest.ps1') && exists('outputs/pipeline-manifest.json') ? 'done' : 'pend',
      source: 'scripts/Write-PipelineManifest.ps1',
      detail: 'Refreshes the live pipeline manifest from repo state.'
    },
    {
      name: 'High-blast-radius prompt guardrails',
      status: exists('.github/prompts/08-code-generator.prompt.md') && exists('.github/prompts/10-code-reviewer.prompt.md') && exists('.github/prompts/11-security-agent.prompt.md') ? 'done' : 'pend',
      source: '.github/prompts/08-code-generator.prompt.md',
      detail: 'Code generation, review, and security prompts include explicit scope and disclosure controls.'
    },
    {
      name: 'Responsible AI disclosure footer',
      status: exists('.github/prompts/12-documentation.prompt.md') && exists('.github/prompts/13-pr-assembler.prompt.md') ? 'done' : 'warn',
      source: '.github/prompts/12-documentation.prompt.md',
      detail: 'Deliverable prompts require a Responsible AI disclosure footer in generated outputs.'
    }
  ];
}

function derivePhase(stepId: string | null): string {
  if (!stepId) {
    return 'Completed';
  }

  if (stepId === '0' || stepId === '00') {
    return 'Initiate';
  }

  if (stepId === '01' || stepId === '02') {
    return 'Analyze';
  }

  if (stepId === '03' || stepId === '04' || stepId === '05') {
    return 'Design';
  }

  if (stepId === '06' || stepId === '07' || stepId === '08') {
    return 'Build';
  }

  if (stepId === '09' || stepId === '10' || stepId === '11' || stepId === '12' || stepId === '13') {
    return 'Validate & Release';
  }

  return 'Delivery';
}

function stepPhase(stepId: string): string {
  if (stepId === '0' || stepId === '00') {
    return 'Initiate';
  }

  if (stepId === '01' || stepId === '02') {
    return 'Analyze';
  }

  if (stepId === '03' || stepId === '04' || stepId === '05') {
    return 'Design';
  }

  if (stepId === '06' || stepId === '07' || stepId === '08') {
    return 'Build';
  }

  return 'Validate & Release';
}

function aiEffortMultiplier(status: StatusTone): number {
  switch (status) {
    case 'done':
      return 0.42;
    case 'warn':
      return 0.58;
    case 'err':
      return 0.74;
    default:
      return 0.55;
  }
}

function roundMetric(value: number): number {
  return Math.round(value * 10) / 10;
}

function buildRoi(steps: DashboardStep[]): PipelineSnapshot['roi'] {
  const hourlyRate = 145;
  const baselineHoursByStep: Record<string, number> = {
    '0': 2,
    '00': 4,
    '01': 6,
    '02': 4,
    '03': 7,
    '04': 8,
    '05': 5,
    '06': 6,
    '07': 4,
    '08': 24,
    '09': 12,
    '10': 6,
    '11': 6,
    '12': 5,
    '13': 4
  };

  const phaseOrder = ['Initiate', 'Analyze', 'Design', 'Build', 'Validate & Release'];
  const phaseMetrics = new Map(phaseOrder.map((phase) => [phase, {
    phase,
    completedStages: 0,
    totalStages: 0,
    humanHours: 0,
    aiHours: 0
  }]));

  let projectedHumanHours = 0;
  let projectedAiHours = 0;
  let realizedHumanHours = 0;
  let realizedAiHours = 0;
  let realizedStageCount = 0;

  for (const step of steps) {
    const phase = stepPhase(step.step);
    const baselineHours = baselineHoursByStep[step.step] ?? 4;
    const aiHours = baselineHours * aiEffortMultiplier(step.status);

    projectedHumanHours += baselineHours;
    projectedAiHours += aiHours;

    if (step.status !== 'pend') {
      realizedHumanHours += baselineHours;
      realizedAiHours += aiHours;
      realizedStageCount += 1;
    }

    const phaseEntry = phaseMetrics.get(phase);
    if (phaseEntry) {
      phaseEntry.totalStages += 1;
      if (step.status === 'done') {
        phaseEntry.completedStages += 1;
      }
      phaseEntry.humanHours += baselineHours;
      phaseEntry.aiHours += aiHours;
    }
  }

  const summarySavedHours = projectedHumanHours - projectedAiHours;
  const summaryAcceleration = projectedHumanHours > 0
    ? (summarySavedHours / projectedHumanHours) * 100
    : 0;

  const realizedSavedHours = realizedHumanHours - realizedAiHours;
  const realizedAcceleration = realizedHumanHours > 0
    ? (realizedSavedHours / realizedHumanHours) * 100
    : 0;

  const phases = phaseOrder
    .map((phase) => phaseMetrics.get(phase))
    .filter((entry): entry is NonNullable<typeof entry> => Boolean(entry))
    .map((entry) => {
      const timeSavedHours = entry.humanHours - entry.aiHours;
      return {
        phase: entry.phase,
        completedStages: entry.completedStages,
        totalStages: entry.totalStages,
        humanHours: roundMetric(entry.humanHours),
        aiHours: roundMetric(entry.aiHours),
        timeSavedHours: roundMetric(timeSavedHours),
        humanCost: Math.round(entry.humanHours * hourlyRate),
        aiCost: Math.round(entry.aiHours * hourlyRate),
        costSaved: Math.round(timeSavedHours * hourlyRate)
      };
    });

  return {
    hourlyRate,
    summary: {
      humanHours: roundMetric(projectedHumanHours),
      aiHours: roundMetric(projectedAiHours),
      timeSavedHours: roundMetric(summarySavedHours),
      accelerationPercent: Math.round(summaryAcceleration),
      humanCost: Math.round(projectedHumanHours * hourlyRate),
      aiCost: Math.round(projectedAiHours * hourlyRate),
      costSaved: Math.round(summarySavedHours * hourlyRate)
    },
    realized: {
      completedStages: realizedStageCount,
      totalStages: steps.length,
      humanHours: roundMetric(realizedHumanHours),
      aiHours: roundMetric(realizedAiHours),
      timeSavedHours: roundMetric(realizedSavedHours),
      accelerationPercent: Math.round(realizedAcceleration),
      humanCost: Math.round(realizedHumanHours * hourlyRate),
      aiCost: Math.round(realizedAiHours * hourlyRate),
      costSaved: Math.round(realizedSavedHours * hourlyRate)
    },
    phases
  };
}

export function loadDashboardSnapshot(): PipelineSnapshot {
  const steps = buildPipelineSteps();
  const checkpoints = buildCheckpoints(steps);
  const governance = parseGovernance();
  const evalSummary = parseEvalSummary();
  const taskLog = parseTaskLog();
  const guardrails = buildGuardrails();
  const roi = buildRoi(steps);
  const manifest = readJson('outputs/pipeline-manifest.json');

  const stepCount = steps.length;
  const stepsCompleted = steps.filter((step) => step.status === 'done').length;
  const checkpointsApproved = checkpoints.filter((checkpoint) => checkpoint.passed).length;
  const nextStep = steps.find((step) => step.status !== 'done') ?? null;
  const phase = derivePhase(nextStep?.step ?? null);
  const pickup = nextStep
    ? `${nextStep.note} Complete ${nextStep.output} using ${nextStep.promptFile.split('/').pop() ?? nextStep.promptFile}.`
    : 'All pipeline stages in current run are complete.';

  return {
    project: {
      name: 'TCPA Regulatory Compliance API',
      client: 'Southern Company Gas',
      phase,
      nextStep: nextStep ? `Step ${nextStep.step} - ${nextStep.name}` : 'None',
      nextPromptFile: nextStep?.promptFile ?? 'N/A',
      pickup,
      branch: manifest.run?.branch ?? 'main',
      overallStatus: manifest.summary?.overall_status ?? 'in_progress',
      model: {
        id: manifest.run?.model?.id ?? 'unknown',
        vendor: manifest.run?.model?.vendor ?? 'unknown',
        displayName: manifest.run?.model?.display_name ?? (manifest.run?.model?.id ?? 'unknown'),
        capturedAt: manifest.run?.model?.captured_at ?? 'unknown'
      }
    },
    stats: {
      stepsCompleted,
      stepsTotal: stepCount,
      checkpointsApproved,
      checkpointsRequired: checkpoints.length,
      openGovernanceItems: governance.openCount,
      openEvalFailures: evalSummary.failCount
    },
    steps,
    checkpoints,
    evalSummary,
    governance,
    guardrails,
    taskLog,
    roi
  };
}
