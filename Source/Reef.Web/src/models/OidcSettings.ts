export interface OidcSettings {
  isEnabled: boolean;
  name: string;
  authority: string;
  clientId: string;
  clientSecret?: string;
  hasClientSecret?: boolean;
  scopes: string;
  usernameClaim: string;
  emailClaim: string;
  createAccounts: boolean;
}
