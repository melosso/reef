import { Activity, CalendarClock, Database, FileCode, FolderTree, LayoutDashboard, MailCheck, Send, Workflow } from '@lucide/svelte';

export const navItems = [
  { href: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { href: '/connections', label: 'Connections', icon: Database },
  { href: '/destinations', label: 'Destinations', icon: Send },
  { href: '/groups', label: 'Groups', icon: FolderTree },
  { href: '/templates', label: 'Templates', icon: FileCode },
  { href: '/profiles', label: 'Profiles', icon: Workflow },
  { href: '/jobs', label: 'Jobs', icon: CalendarClock },
  { href: '/approvals', label: 'Approvals', icon: MailCheck },
  { href: '/executions', label: 'Executions', icon: Activity },
];
