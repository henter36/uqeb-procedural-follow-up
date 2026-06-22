import type { ComponentType, SVGProps } from 'react';
import {
  IconDashboard, IconTransactions, IconReports, IconUsers,
  IconSettings, IconImport, IconSecurity, IconLetter,
} from '../ui/icons';

export type NavItem = {
  path: string;
  label: string;
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  adminOnly?: boolean;
  supervisorOnly?: boolean;
  matchPrefix?: boolean;
};

export type NavSection = {
  label: string;
  items: NavItem[];
};

export const navSections: NavSection[] = [
  {
    label: 'الرئيسية',
    items: [
      { path: '/', label: 'لوحة المتابعة', icon: IconDashboard },
      { path: '/transactions', label: 'المعاملات', icon: IconTransactions, matchPrefix: true },
      { path: '/reports', label: 'التقارير', icon: IconReports },
    ],
  },
  {
    label: 'العمليات',
    items: [
      { path: '/reports?tab=waiting', label: 'التحويلات والردود', icon: IconReports },
      { path: '/letter-template', label: 'قالب خطاب التعقيب', icon: IconLetter, supervisorOnly: true },
      { path: '/transactions/import', label: 'استيراد Excel', icon: IconImport, adminOnly: true },
    ],
  },
  {
    label: 'الإدارة',
    items: [
      { path: '/users', label: 'المستخدمون', icon: IconUsers, adminOnly: true },
      { path: '/departments', label: 'الإدارات', icon: IconSettings, adminOnly: true },
      { path: '/external-parties', label: 'الجهات الخارجية', icon: IconSettings, adminOnly: true },
      { path: '/categories', label: 'التصنيفات', icon: IconSettings, adminOnly: true },
      { path: '/security', label: 'الأمن والتنبيهات', icon: IconSecurity, adminOnly: true },
    ],
  },
];

export type RouteMeta = {
  title: string;
  breadcrumbs: { label: string; path?: string }[];
};

const TRANSACTION_EDIT_REGEX = /^\/transactions\/(\d+)\/edit$/;
const TRANSACTION_DETAIL_REGEX = /^\/transactions\/(\d+)$/;

export function getRouteMeta(pathname: string, search: string): RouteMeta {
  const full = pathname + search;

  const routes: Record<string, RouteMeta> = {
    '/': { title: 'لوحة المتابعة', breadcrumbs: [{ label: 'لوحة المتابعة' }] },
    '/transactions': { title: 'المعاملات', breadcrumbs: [{ label: 'المعاملات' }] },
    '/transactions/new': { title: 'إضافة معاملة', breadcrumbs: [{ label: 'المعاملات', path: '/transactions' }, { label: 'إضافة معاملة' }] },
    '/transactions/import': { title: 'استيراد المعاملات', breadcrumbs: [{ label: 'المعاملات', path: '/transactions' }, { label: 'استيراد' }] },
    '/reports': { title: 'التقارير', breadcrumbs: [{ label: 'التقارير' }] },
    '/letter-template': { title: 'قالب خطاب التعقيب', breadcrumbs: [{ label: 'قالب خطاب التعقيب' }] },
    '/users': { title: 'المستخدمون', breadcrumbs: [{ label: 'الإدارة' }, { label: 'المستخدمون' }] },
    '/departments': { title: 'الإدارات', breadcrumbs: [{ label: 'الإدارة' }, { label: 'الإدارات' }] },
    '/external-parties': { title: 'الجهات الخارجية', breadcrumbs: [{ label: 'الإدارة' }, { label: 'الجهات الخارجية' }] },
    '/categories': { title: 'التصنيفات', breadcrumbs: [{ label: 'الإدارة' }, { label: 'التصنيفات' }] },
    '/security': { title: 'الأمن والتنبيهات', breadcrumbs: [{ label: 'الإدارة' }, { label: 'الأمن والتنبيهات' }] },
  };

  if (routes[pathname]) return routes[pathname];

  if (TRANSACTION_EDIT_REGEX.exec(pathname)) {
    return {
      title: 'تعديل المعاملة',
      breadcrumbs: [
        { label: 'المعاملات', path: '/transactions' },
        { label: 'تعديل المعاملة' },
      ],
    };
  }

  if (TRANSACTION_DETAIL_REGEX.exec(pathname)) {
    return {
      title: 'تفاصيل المعاملة',
      breadcrumbs: [
        { label: 'المعاملات', path: '/transactions' },
        { label: 'تفاصيل المعاملة' },
      ],
    };
  }

  if (full.startsWith('/reports?')) {
    return { title: 'التقارير', breadcrumbs: [{ label: 'التقارير' }] };
  }

  return { title: 'المتابعة الإجرائية', breadcrumbs: [] };
}

function normalizeSearch(search: string): string {
  if (!search) return '';
  return search.startsWith('?') ? search : `?${search}`;
}

export function isNavActive(item: NavItem, pathname: string, search: string): boolean {
  if (item.path === '/') return pathname === '/';

  const [itemPathname, itemQuery = ''] = item.path.split('?');
  const normalizedSearch = normalizeSearch(search);
  const normalizedItemQuery = itemQuery ? `?${itemQuery}` : '';

  if (normalizedItemQuery) {
    return pathname === itemPathname && normalizedSearch === normalizedItemQuery;
  }

  if (item.matchPrefix) {
    return pathname === itemPathname || pathname.startsWith(`${itemPathname}/`);
  }

  if (pathname !== itemPathname) return false;

  if (!normalizedItemQuery) {
    const currentTab = new URLSearchParams(normalizedSearch.slice(1)).get('tab');
    return !currentTab;
  }

  return false;
}
