export type DetailTab = 'details' | 'timeline' | 'audit';

const LEGACY_DETAILS_TABS = new Set(['overview', 'assignments', 'followups', 'attachments']);

export function parseDetailTab(tabFromUrl: string | null): DetailTab {
  if (tabFromUrl === 'timeline') return 'timeline';
  if (tabFromUrl === 'audit') return 'audit';
  if (tabFromUrl && LEGACY_DETAILS_TABS.has(tabFromUrl)) return 'details';
  return 'details';
}

export function isLegacyDetailsTab(tabFromUrl: string | null): boolean {
  return Boolean(tabFromUrl && LEGACY_DETAILS_TABS.has(tabFromUrl));
}
