export interface Interpreter {
  name: string;
  displayName: string;
  available: boolean;
  error?: string | null;
  output?: string | null;
}
