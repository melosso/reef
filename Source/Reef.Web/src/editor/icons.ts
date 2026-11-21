import {
  ArrowRightLeft,
  Clock,
  GitBranch,
  Merge,
  MousePointerClick,
  Repeat,
  Terminal,
  Webhook,
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
  type LucideIcon,
} from '@lucide/svelte';

const ICONS: Record<string, LucideIcon> = {
  'arrow-right-left': ArrowRightLeft,
  clock: Clock,
  'git-branch': GitBranch,
  merge: Merge,
  'mouse-pointer-click': MousePointerClick,
  repeat: Repeat,
  terminal: Terminal,
  webhook: Webhook,
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

export function stepIcon(name: string): LucideIcon {
  return ICONS[name] ?? Workflow;
}
