import { Activity, CalendarClock, Database, FileCode, LayoutDashboard, MailCheck, Send, Workflow } from '@lucide/svelte';

export const navItems = [
  { href: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { href: '/connections', label: 'Connections', icon: Database },
  { href: '/destinations', label: 'Destinations', icon: Send },
  { href: '/templates', label: 'Templates', icon: FileCode },
  { href: '/workflows', label: 'Workflows', icon: Workflow },
  { href: '/jobs', label: 'Jobs', icon: CalendarClock },
  { href: '/approvals', label: 'Approvals', icon: MailCheck },
  { href: '/executions', label: 'Executions', icon: Activity },
];
