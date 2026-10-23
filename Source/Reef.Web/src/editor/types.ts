export interface FieldOption {
  value: string | number | boolean;
  label: string;
}

export interface CatalogField {
  key: string;
  label: string;
  kind: 'text' | 'password' | 'textarea' | 'code' | 'number' | 'bool' | 'select' | 'list' | 'json' | 'table';
  tooltip?: string;
  placeholder?: string;
  required?: boolean;
  hidden?: boolean;
  format?: string;
  options?: FieldOption[];
  optionsSource?: string;
  showWhen?: { key: string; values?: string[] | null };
  default?: unknown;
  columns?: CatalogField[];
}

export interface NodeDefinition {
  type: string;
  label: string;
  icon: string;
  tooltip: string;
  rank: number;
  required?: boolean;
  fields: CatalogField[];
}

export interface Catalog {
  kind: 'export' | 'import';
  settings: CatalogField[];
  nodes: NodeDefinition[];
}

export interface DocNode {
  id: string;
  type: string;
  x: number;
  y: number;
  config?: Record<string, any>;
}

export interface ProfileDoc {
  kind: 'export' | 'import';
  id?: number | null;
  code?: string | null;
  settings: Record<string, any>;
  nodes: DocNode[];
}

export type Options = Record<string, FieldOption[]>;
