import { describe, expect, it } from 'vitest';
import { canonicalUrl } from '../src/lib/routes';

describe('canonicalUrl', () => {
  it.each([
    ['/', '', '/login'],
    ['/index', '?next=%2Fjobs', '/login?next=%2Fjobs'],
    ['/index', '?sso=denied', '/login?sso=denied'],
    ['/logoff', '', '/logout'],
    ['/connections', '?id=5', '/connections/5'],
    ['/destinations', '?id=3', '/destinations/3'],
    ['/templates', '?id=7', '/templates/7'],
    ['/jobs', '?id=2', '/jobs/2'],
    ['/executions', '?id=01ARZ3NDEKTSV4RRFFQ69G5FAV', '/executions/01ARZ3NDEKTSV4RRFFQ69G5FAV'],
    ['/profiles', '', '/workflows'],
    ['/profiles/export/01ARZ3NDEKTSV4RRFFQ69G5FAV', '', '/workflows'],
    ['/email-approvals', '', '/approvals'],
    ['/email-approvals', '?tab=history', '/approvals/history'],
    ['/admin', '?tab=users', '/admin/users'],
    ['/admin', '?tab=apikeys', '/admin/api-keys'],
    ['/account', '?tab=mfa', '/account/security'],
    ['/account', '?tab=password', '/account/password'],
    ['/Jobs/', '', '/jobs'],
    ['/connections/', '?page=2', '/connections?page=2'],
  ])('%s%s becomes %s', (path, search, expected) => {
    expect(canonicalUrl(path, search)).toBe(expected);
  });

  it.each([
    ['/dashboard', ''],
    ['/jobs/2', ''],
    ['/executions/01ARZ3NDEKTSV4RRFFQ69G5FAV', '?page=2'],
    ['/admin/users', ''],
    ['/workflows/new', ''],
  ])('%s%s is already canonical', (path, search) => {
    expect(canonicalUrl(path, search)).toBeNull();
  });
});
