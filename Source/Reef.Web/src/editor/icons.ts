import {
  ArrowRightLeft,
  CircleCheck,
  Copy,
  Database,
  Download,
  FileCode,
  FileText,
  HardDriveUpload,
  Mail,
  Play,
  RefreshCw,
  Split,
  Workflow,
} from '@lucide/svelte';

const ICONS: Record<string, any> = {
  'arrow-right-left': ArrowRightLeft,
  'check-circle': CircleCheck,
  copy: Copy,
  database: Database,
  download: Download,
  'file-code': FileCode,
  'file-text': FileText,
  'hard-drive-upload': HardDriveUpload,
  mail: Mail,
  play: Play,
  'refresh-cw': RefreshCw,
  split: Split,
};

export function stepIcon(name: string): any {
  return ICONS[name] ?? Workflow;
}
