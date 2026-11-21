import type { CatalogField } from '../editor/types';

export interface WorkflowNodeDefinition {
  type: string;
  label: string;
  icon: string;
  tooltip: string;
  group: string;
  inputs: string[];
  outputs: string[];
  fields: CatalogField[];
}
