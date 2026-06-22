import type { ComponentType } from 'react';
import type { SVGProps } from 'react';
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

  const editMatch = pathname.match(/^\/transactions\/(\d+)\/edit$/);
  if (editMatch) {
    return {
      title: 'تعديل المعاملة',
      breadcrumbs: [
        { label: 'المعاملات', path: '/transactions' },
        { label: 'تعديل المعاملة' },
      ],
    };
  }

  const detailMatch = pathname.match(/^\/transactions\/(\d+)$/);
  if (detailMatch) {
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

export function isNavActive(item: NavItem, pathname: string): boolean {
  if (item.path === '/') return pathname === '/';
  const itemPath = item.path.split('?')[0];
  if (item.matchPrefix) return pathname === itemPath || pathname.startsWith(itemPath + '/');
  return pathname === itemPath;
}
