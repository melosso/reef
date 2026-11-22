import type { WorkflowEdge } from './WorkflowEdge';
import type { WorkflowNode } from './WorkflowNode';

export interface Workflow {
  id?: string | null;
  name: string;
  version?: number;
  active: boolean;
  maxPasses: number;
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
  updatedAt?: string | null;
}
