export interface ApiKey {
  id: number;
  name: string;
  permissions: string;
  createdAt: string;
  expiresAt?: string | null;
  lastUsedAt?: string | null;
  isActive: boolean;
}
