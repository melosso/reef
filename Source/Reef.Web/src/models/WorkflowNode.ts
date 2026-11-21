export interface WorkflowNode {
  id: string;
  type: string;
  name?: string | null;
  x: number;
  y: number;
  config: Record<string, unknown>;
}
