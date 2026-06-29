import type { ComponentType, SVGProps } from 'react';
import {
  IconDashboard, IconTransactions, IconReports, IconUsers,
  IconSettings, IconImport, IconSecurity, IconLetter, IconPrint,
} from '../ui/icons';
import { isInstitutionalReportsEnabled } from '../../config/featureFlags';

export type NavItem = {
  path: string;
  label: string;
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  adminOnly?: boolean;
  supervisorOnly?: boolean;
  followUpPrintOnly?: boolean;
  departmentUserOnly?: boolean;
  departmentResponseReviewOnly?: boolean;
  hideForDepartmentUser?: boolean;
  matchPrefix?: boolean;
  badgeKey?: 'pendingPrints';
};

export type NavSection = {
  label: string;
  items: NavItem[];
};

export function buildNavSections(institutionalReportsEnabled = isInstitutionalReportsEnabled()): NavSection[] {
  const sections: NavSection[] = [
    {
      label: 'الرئيسية',
      items: [
        { path: '/', label: 'لوحة المتابعة', icon: IconDashboard },
        { path: '/transactions', label: 'المعاملات', icon: IconTransactions, matchPrefix: true, hideForDepartmentUser: true },
        { path: '/reports', label: 'التقارير', icon: IconReports, hideForDepartmentUser: true },
        ...(institutionalReportsEnabled
          ? [{ path: '/report-builder', label: 'منشئ التقارير', icon: IconReports, adminOnly: true } satisfies NavItem]
          : []),
      ],
    },
    {
      label: 'إفادات الإدارات',
      items: [
        { path: '/department-responses', label: 'معاملات إدارتي', icon: IconTransactions, departmentUserOnly: true, matchPrefix: false },
        { path: '/department-responses/review', label: 'إفادات بانتظار المراجعة', icon: IconReports, departmentResponseReviewOnly: true },
      ],
    },
    {
      label: 'العمليات',
      items: [
        { path: '/reports?tab=waiting', label: 'التحويلات والردود', icon: IconReports, hideForDepartmentUser: true },
        { path: '/letter-template', label: 'قوالب خطاب التعقيب', icon: IconLetter, supervisorOnly: true, matchPrefix: true },
        { path: '/follow-up-print/eligible', label: 'طباعة التعقيب — المستحقة', icon: IconPrint, followUpPrintOnly: true, matchPrefix: true },
        { path: '/follow-up-print/jobs', label: 'مهام طباعة التعقيب', icon: IconPrint, followUpPrintOnly: true, matchPrefix: true },
        { path: '/follow-up-print/pending', label: 'بانتظار تسجيل التعقيب', icon: IconPrint, followUpPrintOnly: true, badgeKey: 'pendingPrints' },
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

  return sections;
}

export type RouteMeta = {
  title: string;
  breadcrumbs: { label: string; path?: string }[];
};

const TRANSACTION_EDIT_REGEX = /^\/transactions\/(\d+)\/edit$/;
const TRANSACTION_DETAIL_REGEX = /^\/transactions\/(\d+)$/;
const FOLLOW_UP_PRINT_JOB_REGEX = /^\/follow-up-print\/jobs\/(\d+)$/;
const FOLLOW_UP_PRINT_PART_REGEX = /^\/follow-up-print\/parts\/(\d+)\/(\d+)\/print$/;

export function getRouteMeta(pathname: string, search: string): RouteMeta {
  const full = pathname + search;

  const routes: Record<string, RouteMeta> = {
    '/': { title: 'لوحة المتابعة', breadcrumbs: [{ label: 'لوحة المتابعة' }] },
    '/transactions': { title: 'المعاملات', breadcrumbs: [{ label: 'المعاملات' }] },
    '/transactions/new': { title: 'إضافة معاملة', breadcrumbs: [{ label: 'المعاملات', path: '/transactions' }, { label: 'إضافة معاملة' }] },
    '/transactions/import': { title: 'استيراد المعاملات', breadcrumbs: [{ label: 'المعاملات', path: '/transactions' }, { label: 'استيراد' }] },
    '/reports': { title: 'التقارير', breadcrumbs: [{ label: 'التقارير' }] },
    '/letter-template': { title: 'قوالب خطاب التعقيب', breadcrumbs: [{ label: 'قوالب خطاب التعقيب' }] },
    '/follow-up-print/eligible': { title: 'المعاملات المستحقة للتعقيب', breadcrumbs: [{ label: 'طباعة التعقيب' }, { label: 'المستحقة' }] },
    '/follow-up-print/jobs': { title: 'مهام طباعة التعقيب', breadcrumbs: [{ label: 'طباعة التعقيب' }, { label: 'المهام' }] },
    '/follow-up-print/pending': { title: 'بانتظار تسجيل التعقيب', breadcrumbs: [{ label: 'طباعة التعقيب' }, { label: 'بانتظار تسجيل التعقيب' }] },
    '/users': { title: 'المستخدمون', breadcrumbs: [{ label: 'الإدارة' }, { label: 'المستخدمون' }] },
    '/departments': { title: 'الإدارات', breadcrumbs: [{ label: 'الإدارة' }, { label: 'الإدارات' }] },
    '/external-parties': { title: 'الجهات الخارجية', breadcrumbs: [{ label: 'الإدارة' }, { label: 'الجهات الخارجية' }] },
    '/categories': { title: 'التصنيفات', breadcrumbs: [{ label: 'الإدارة' }, { label: 'التصنيفات' }] },
    '/security': { title: 'الأمن والتنبيهات', breadcrumbs: [{ label: 'الإدارة' }, { label: 'الأمن والتنبيهات' }] },
    '/department-responses': { title: 'معاملات إدارتي', breadcrumbs: [{ label: 'إفادات الإدارات' }, { label: 'معاملات إدارتي' }] },
    '/department-responses/review': { title: 'إفادات بانتظار المراجعة', breadcrumbs: [{ label: 'إفادات الإدارات' }, { label: 'بانتظار المراجعة' }] },
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

  const jobMatch = FOLLOW_UP_PRINT_JOB_REGEX.exec(pathname);
  if (jobMatch) {
    return {
      title: `مهمة الطباعة #${jobMatch[1]}`,
      breadcrumbs: [
        { label: 'طباعة التعقيب', path: '/follow-up-print/jobs' },
        { label: `مهمة #${jobMatch[1]}` },
      ],
    };
  }

  const partMatch = FOLLOW_UP_PRINT_PART_REGEX.exec(pathname);
  if (partMatch) {
    return {
      title: `طباعة الجزء ${partMatch[2]}`,
      breadcrumbs: [
        { label: 'طباعة التعقيب', path: '/follow-up-print/jobs' },
        { label: `مهمة #${partMatch[1]}`, path: `/follow-up-print/jobs/${partMatch[1]}` },
        { label: `الجزء ${partMatch[2]}` },
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
    if (itemPathname === '/reports') {
      const currentTab = new URLSearchParams(normalizedSearch.slice(1)).get('tab');
      return !currentTab;
    }
    return true;
  }

  return false;
}
