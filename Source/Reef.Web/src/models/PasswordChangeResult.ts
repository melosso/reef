export interface PasswordChangeResult {
  success?: boolean;
  message?: string | null;
  requiresLogoff?: boolean;
}
