export interface Destination {
  id: number;
  name: string;
  description: string;
  type: string;
  configurationJson: string;
  isActive: boolean;
  tags: string;
  createdAt: string;
  modifiedAt?: string | null;
}
