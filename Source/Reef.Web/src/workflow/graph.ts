import { textOf } from '../editor/config';
import type { ItemsSample } from '../models/ItemsSample';
import type { NodeRun } from '../models/NodeRun';
import type { Workflow } from '../models/Workflow';
import type { WorkflowNodeDefinition } from '../models/WorkflowNodeDefinition';

export const PALETTE_ID = 'workflow-steps';

export const GROUPS = ['Triggers', 'Sources', 'Transform', 'Flow', 'Deliver', 'Hooks'];

export function outputsOf(def: WorkflowNodeDefinition, config: Record<string, unknown>): string[] {
  if (def.type !== 'switch') return def.outputs;
  const cases = Array.isArray(config.Cases) ? (config.Cases as Record<string, unknown>[]) : [];
  const values = cases.map((c) => textOf(c.Value)).filter((v) => v.length > 0);
  return [...new Set(values), 'fallback'];
}

export function nextId(type: string, workflow: Workflow): string {
  const taken = new Set(workflow.nodes.map((n) => n.id));
  for (let i = 1; ; i++) if (!taken.has(`${type}${i}`)) return `${type}${i}`;
}

export function samples(run: NodeRun | undefined): Record<string, ItemsSample> {
  if (!run?.outputSampleJson) return {};
  try {
    return JSON.parse(run.outputSampleJson) as Record<string, ItemsSample>;
  } catch {
    return {};
  }
}

export function lastPass(runs: NodeRun[], nodeId: string): NodeRun | undefined {
  return runs.filter((r) => r.nodeId === nodeId).at(-1);
}

// Snapshot before change same keys merge
export class History {
  #past: string[] = [];
  #future: string[] = [];
  #key: string | null = null;

  record(before: string, key: string | null = null) {
    if (key && key === this.#key) return;
    this.#key = key;
    this.#past.push(before);
    if (this.#past.length > 100) this.#past.shift();
    this.#future = [];
  }

  undo(current: string): string | null {
    const previous = this.#past.pop();
    if (previous === undefined) return null;
    this.#future.push(current);
    this.#key = null;
    return previous;
  }

  redo(current: string): string | null {
    const next = this.#future.pop();
    if (next === undefined) return null;
    this.#past.push(current);
    this.#key = null;
    return next;
  }

  get canUndo() {
    return this.#past.length > 0;
  }

  get canRedo() {
    return this.#future.length > 0;
  }
}
