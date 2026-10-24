export interface TotpConfirmResult {
  success?: boolean;
  message?: string | null;
  backupCodes?: string[];
}
