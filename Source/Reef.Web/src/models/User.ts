export interface User {
  id: string;
  isFirstAdmin?: boolean;
  username: string;
  displayName?: string | null;
  email?: string | null;
  role: string;
  isActive: boolean;
  createdAt: string;
  lastLoginAt?: string | null;
  lastSeenAt?: string | null;
  passwordChangeRequired: boolean;
  mfaEnabled: boolean;
  mfaMethod?: string | null;
}
