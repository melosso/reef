import { api } from './api';
import { confirm } from './confirm.svelte';
import { notify } from './notify';
import type { Workflow } from '../models/Workflow';
import type { WorkflowExecution } from '../models/WorkflowExecution';

export function sendsEmail(workflow: Workflow): boolean {
  return workflow.nodes.some((n) => n.type === 'email' && n.config?.RequireApproval !== true);
}

// Email steps without approval ask first
export async function runWorkflow(workflow: Workflow): Promise<WorkflowExecution | null> {
  if (
    sendsEmail(workflow) &&
    !(await confirm({
      title: `Send emails for ${workflow.name}?`,
      message: 'The workflow sends one email per row, or per group, to the recipients in the rows.',
      confirm: 'Send emails',
      danger: true,
    }))
  )
    return null;
  const run = await api.post<WorkflowExecution>(`/api/workflows/${workflow.id}/run`);
  if (run.status === 'Failed') notify.error(`${workflow.name}: ${run.errorMessage ?? 'Run failed'}`, { key: `run-${workflow.id}` });
  else notify.success(`${workflow.name} ran`, { key: `run-${workflow.id}` });
  return run;
}
