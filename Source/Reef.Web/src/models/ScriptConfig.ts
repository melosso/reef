export interface ScriptConfig {
  Interpreter?: string;
  ScriptPathOrInline?: string;
  ScriptIsInline?: boolean;
  Timeout?: number;
  EnvAllowlist?: string | null;
}
