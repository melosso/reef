import { describe, expect, it } from 'vitest';
import type { Workflow } from '../src/models/Workflow';
import type { WorkflowNodeDefinition } from '../src/models/WorkflowNodeDefinition';
import { History, nextId, outputsOf } from '../src/workflow/graph';

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
});
