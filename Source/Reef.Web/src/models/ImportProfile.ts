export interface ImportProfile {
  id: string;
  code: string;
  name: string;
  groupId?: string | null;
  groupName?: string | null;
  sourceType: string;
  sourceFormat: string;
  sourceDestinationId?: string | null;
  sourceDestinationName?: string | null;
  targetType: string;
  targetConnectionId?: string | null;
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
