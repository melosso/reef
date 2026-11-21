import type { NodeRun } from './NodeRun';

export interface WorkflowExecution {
  id: string;
  workflowId: string;
  workflowVersion: number;
  triggerNodeId: string;
  status: string;
  startedAt: string;
  completedAt?: string | null;
  errorMessage?: string | null;
  nodes: NodeRun[];
}
