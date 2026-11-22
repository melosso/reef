import type { ItemsSample } from './ItemsSample';

export interface NodeTestResult {
  outputs: Record<string, ItemsSample>;
  warning?: string | null;
}
