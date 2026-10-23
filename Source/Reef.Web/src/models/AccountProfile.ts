export interface AccountProfile {
  username: string;
  displayName?: string | null;
  email?: string | null;
  mfaEnabled?: boolean;
  mfaMethod?: string | null;
  emailMfaAvailable?: boolean;
}
