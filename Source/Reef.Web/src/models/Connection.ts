export interface Connection {
  id: string;
  name: string;
  type: string;
  connectionString: string;
  isActive: boolean;
  tags?: string | null;
  createdAt: string;
  updatedAt: string;
  lastTestedAt?: string | null;
  lastTestResult?: string | null;
}
