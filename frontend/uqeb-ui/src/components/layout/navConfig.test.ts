import { describe, expect, it } from 'vitest';
import { isNavActive, buildNavSections, type NavItem } from './navConfig';

function findItem(path: string): NavItem {
  const item = buildNavSections().flatMap((s) => s.items).find((i) => i.path === path);
  if (!item) throw new Error(`Nav item not found: ${path}`);
  return item;
}

describe('isNavActive', () => {
  it('activates /reports without query when no tab is present', () => {
    const item = findItem('/reports');
    expect(isNavActive(item, '/reports', '')).toBe(true);
  });

  it('activates /reports?tab=waiting for the waiting tab item', () => {
    const waitingItem = findItem('/reports?tab=waiting');
    expect(isNavActive(waitingItem, '/reports', '?tab=waiting')).toBe(true);
  });

  it('does not activate base /reports when a specific tab is active', () => {
    const reportsItem = findItem('/reports');
    expect(isNavActive(reportsItem, '/reports', '?tab=waiting')).toBe(false);
    expect(isNavActive(reportsItem, '/reports', '?tab=partial')).toBe(false);
  });

  it('does not activate waiting item for a different tab', () => {
    const waitingItem = findItem('/reports?tab=waiting');
    expect(isNavActive(waitingItem, '/reports', '?tab=partial')).toBe(false);
  });

  it('supports matchPrefix for transactions routes', () => {
    const item = findItem('/transactions');
    expect(isNavActive(item, '/transactions', '')).toBe(true);
    expect(isNavActive(item, '/transactions/42', '')).toBe(true);
    expect(isNavActive(item, '/transactions/42/edit', '')).toBe(true);
    expect(isNavActive(item, '/reports', '')).toBe(false);
  });

  it('activates home only on exact root path', () => {
    const home = findItem('/');
    expect(isNavActive(home, '/', '')).toBe(true);
    expect(isNavActive(home, '/transactions', '')).toBe(false);
  });

  it('keeps /users active when a tab query is present', () => {
    const usersItem = findItem('/users');
    expect(isNavActive(usersItem, '/users', '?tab=permissions')).toBe(true);
  });

  it('keeps /security active when a tab query is present', () => {
    const securityItem = findItem('/security');
    expect(isNavActive(securityItem, '/security', '?tab=alerts')).toBe(true);
  });
});
