import type { PermissionCode } from './permissions';

export type PermissionEntry = {
  code: PermissionCode;
  label: string;
};

export type PermissionGroup = {
  title: string;
  permissions: PermissionEntry[];
};

type PermissionGroupDefinition = readonly [
  title: string,
  permissions: readonly (readonly [PermissionCode, string])[],
];

const permissionGroupDefinitions: readonly PermissionGroupDefinition[] = [
  ['لوحة المتابعة', [['DashboardView', 'عرض']]],
  ['المعاملات', [
    ['TransactionsView', 'عرض'],
    ['TransactionsCreate', 'إنشاء'],
    ['TransactionsEdit', 'تعديل'],
    ['TransactionsCancel', 'إلغاء/إغلاق'],
    ['TransactionsExport', 'تصدير'],
    ['TransactionDetailsView', 'عرض التفاصيل'],
    ['TransactionAssignmentsCreate', 'إنشاء الإحالات'],
    ['TransactionResponsesEdit', 'تعديل الردود'],
    ['TransactionAttachmentsManage', 'إدارة المرفقات'],
  ]],
  ['التقارير', [
    ['ReportsView', 'عرض'],
    ['ReportsBuild', 'إنشاء تقرير'],
    ['ReportsExportPdf', 'تصدير PDF'],
    ['ReportsExportExcel', 'تصدير Excel'],
    ['ReportsTemplatesManage', 'إدارة القوالب'],
  ]],
  ['جودة البيانات', [
    ['DataQualityView', 'عرض'],
    ['DataQualityReview', 'تعليم المراجعة'],
  ]],
  ['طباعة التعقيب', [
    ['FollowUpPrintView', 'عرض'],
    ['FollowUpPrintCreate', 'إنشاء'],
    ['FollowUpPrintExport', 'تصدير/طباعة'],
  ]],
  ['البيانات المرجعية', [
    ['LookupsView', 'عرض'],
    ['LookupsManage', 'إدارة'],
  ]],
  ['المستخدمون والصلاحيات', [
    ['UsersView', 'عرض المستخدمين'],
    ['UsersManage', 'إدارة المستخدمين'],
    ['UserPermissionsManage', 'إدارة الصلاحيات'],
  ]],
  ['إعدادات النظام', [
    ['SystemSettingsView', 'عرض'],
    ['SystemSettingsManage', 'إدارة'],
  ]],
];

export const permissionGroups: PermissionGroup[] = permissionGroupDefinitions.map(([title, permissions]) => ({
  title,
  permissions: permissions.map(([code, label]) => ({ code, label })),
}));

export const knownPermissions = new Set<PermissionCode>(
  permissionGroups.flatMap((group) => group.permissions.map((permission) => permission.code)),
);

export function keepKnownPermissions(values: readonly string[]): PermissionCode[] {
  return values.filter((value): value is PermissionCode =>
    knownPermissions.has(value as PermissionCode));
}
