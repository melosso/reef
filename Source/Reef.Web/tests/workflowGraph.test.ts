import { describe, expect, it } from 'vitest';
import type { Workflow } from '../src/models/Workflow';
import type { WorkflowNodeDefinition } from '../src/models/WorkflowNodeDefinition';
import { History, nextId, outputsOf, tidy } from '../src/workflow/graph';

const workflow = (ids: string[]): Workflow => ({
  name: 'w',
  active: false,
  maxPasses: 1000,
  nodes: ids.map((id) => ({ id, type: 'query', x: 0, y: 0, config: {} })),
  edges: [],
});

describe('workflow graph helpers', () => {
  it('returns switch outputs from its cases and fallback', () => {
    const def = { type: 'switch', outputs: ['fallback'] } as WorkflowNodeDefinition;
    expect(outputsOf(def, { Cases: [{ Value: 'north' }, { Value: '' }, { Value: 'north' }] })).toEqual(['north', 'fallback']);
  });

  it('picks the first free id for a node type', () => {
    expect(nextId('query', workflow(['query1', 'query2']))).toBe('query3');
  });

  it('undoes and redoes snapshots and merges edits with the same key', () => {
    const history = new History();
    history.record('a', 'field');
    history.record('a b', 'field');
    const undone = history.undo('a b c')!;
    expect(undone).toBe('a');
    expect(history.canUndo).toBe(false);
    expect(history.redo(undone)).toBe('a b c');
  });

  it('tidies steps into rank columns and wraps long chains into rows', () => {
    const ids = Array.from({ length: 10 }, (_, i) => `s${i}`);
    const chain = workflow(ids);
    chain.edges = ids.slice(1).map((id, i) => ({ from: ids[i], fromPort: 'main', to: id, toPort: 'main' }));
    chain.edges.push({ from: 's9', fromPort: 'main', to: 's2', toPort: 'main' });
    tidy(chain);
    const at = (id: string) => chain.nodes.find((n) => n.id === id)!;
    expect([at('s0').x, at('s1').x, at('s7').x]).toEqual([0, 300, 2100]);
    expect(at('s8').x).toBe(0);
    expect(at('s8').y).toBeGreaterThan(at('s7').y);

    const branch = workflow(['a', 'b', 'c']);
    branch.edges = [
      { from: 'a', fromPort: 'main', to: 'b', toPort: 'main' },
      { from: 'a', fromPort: 'main', to: 'c', toPort: 'main' },
    ];
    tidy(branch);
    expect(branch.nodes.map((n) => [n.x, n.y])).toEqual([
      [0, 0],
      [300, 0],
      [300, 160],
    ]);
  });
});
