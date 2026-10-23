import { Activity, CalendarClock, Database, FileCode, FolderTree, LayoutDashboard, MailCheck, Send, Workflow } from '@lucide/svelte';

export const navItems = [
  { href: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { href: '/profiles', label: 'Profiles', icon: Workflow },
  { href: '/connections', label: 'Connections', icon: Database },
  { href: '/destinations', label: 'Destinations', icon: Send },
  { href: '/templates', label: 'Templates', icon: FileCode },
  { href: '/jobs', label: 'Jobs', icon: CalendarClock },
  { href: '/executions', label: 'Executions', icon: Activity },
  { href: '/email-approvals', label: 'Approvals', icon: MailCheck },
  { href: '/groups', label: 'Groups', icon: FolderTree },
];
