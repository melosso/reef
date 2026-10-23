export interface ImportProfile {
  id: number;
  code: string;
  name: string;
  groupId?: number | null;
  groupName?: string | null;
  sourceType: string;
  sourceFormat: string;
  sourceDestinationId?: number | null;
  sourceDestinationName?: string | null;
  targetType: string;
  targetConnectionId?: number | null;
  targetConnectionName?: string | null;
  targetTable: string;
  localTargetFormat?: string | null;
  loadStrategy: string;
  deltaSyncEnabled: boolean;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  lastExecutedAt?: string | null;
}
