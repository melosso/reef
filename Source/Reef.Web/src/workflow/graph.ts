import { textOf } from '../editor/config';
import type { ItemsSample } from '../models/ItemsSample';
import type { NodeRun } from '../models/NodeRun';
import type { Workflow } from '../models/Workflow';
import type { WorkflowNodeDefinition } from '../models/WorkflowNodeDefinition';

export const PALETTE_ID = 'workflow-steps';

const COLUMN = 300;
const ROW = 160;
const PER_ROW = 8;

// Ranks steps by longest path and wraps every eight ranks
export function tidy(workflow: Workflow): void {
  const next = new Map<string, string[]>();
  for (const e of workflow.edges) next.set(e.from, [...(next.get(e.from) ?? []), e.to]);
  const incoming = new Set(workflow.edges.map((e) => e.to));
  const rank = new Map<string, number>();
  const path = new Set<string>();
  const visit = (id: string, r: number) => {
    if (path.has(id) || (rank.get(id) ?? -1) >= r) return;
    rank.set(id, r);
    path.add(id);
    for (const to of next.get(id) ?? []) visit(to, r + 1);
    path.delete(id);
  };
  workflow.nodes.filter((n) => !incoming.has(n.id)).forEach((n) => visit(n.id, 0));
  workflow.nodes.forEach((n) => rank.has(n.id) || visit(n.id, 0));
  const columns = new Map<number, typeof workflow.nodes>();
  for (const n of workflow.nodes) columns.set(rank.get(n.id)!, [...(columns.get(rank.get(n.id)!) ?? []), n]);
  const last = Math.max(0, ...rank.values());
  let top = 0;
  for (let row = 0; row * PER_ROW <= last; row++) {
    let tallest = 1;
    for (let col = 0; col < PER_ROW; col++) {
      const column = columns.get(row * PER_ROW + col) ?? [];
      column.forEach((n, i) => {
        n.x = col * COLUMN;
        n.y = top + i * ROW;
      });
      tallest = Math.max(tallest, column.length);
    }
    top += tallest * ROW + 80;
  }
}

// Unnamed steps of one type get a number
export function stepTitle(workflow: Workflow, node: Workflow['nodes'][number], label: string): string {
  if (node.name) return node.name;
  const same = workflow.nodes.filter((n) => n.type === node.type && !n.name);
  return same.length > 1 ? `${label} ${same.indexOf(node) + 1}` : label;
}

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
