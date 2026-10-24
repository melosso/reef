export interface ApiKey {
  id: string;
  name: string;
  permissions: string;
  createdAt: string;
  expiresAt?: string | null;
  lastUsedAt?: string | null;
  isActive: boolean;
}
