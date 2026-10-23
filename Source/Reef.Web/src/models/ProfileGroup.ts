export interface ProfileGroup {
  id: number;
  name: string;
  parentId?: number | null;
  description?: string | null;
  sortOrder: number;
}
