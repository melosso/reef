export interface QueryTemplate {
  id: number;
  name: string;
  description: string;
  type: string;
  template: string;
  outputFormat: string;
  isActive: boolean;
  tags: string;
  createdAt: string;
  modifiedAt?: string | null;
}
