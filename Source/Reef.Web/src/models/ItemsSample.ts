import type { WorkflowFile } from './WorkflowFile';

export interface ItemsSample {
  columns: { name: string; type: string }[];
  items: Record<string, unknown>[];
  files: WorkflowFile[];
  total: number;
}
