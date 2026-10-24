export interface ProfileGroup {
  id: string;
  name: string;
  parentId?: string | null;
  description?: string | null;
  sortOrder: number;
}
