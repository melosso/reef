import type { SessionData } from '../lib/session.svelte';

export interface LoginResult extends SessionData {
  mfaRequired?: boolean;
  mfaSessionId?: string;
  mfaMethod?: string;
}
