import type { PermissionCode } from './permissions';

export type PermissionEntry = {
  code: PermissionCode;
  label: string;
};

export type PermissionGroup = {
  title: string;
  permissions: PermissionEntry[];
};

export const permissionGroups: PermissionGroup[] = [
  {
    title: 'لوحة المتابعة',
    permissions: [{ code: 'DashboardView', label: 'عرض' }],
  },
  {
    title: 'المعاملات',
    permissions: [
      { code: 'TransactionsView', label: 'عرض' },
      { code: 'TransactionsCreate', label: 'إنشاء' },
      { code: 'TransactionsEdit', label: 'تعديل' },
      { code: 'TransactionsCancel', label: 'إلغاء/إغلاق' },
      { code: 'TransactionsExport', label: 'تصدير' },
      { code: 'TransactionDetailsView', label: 'عرض التفاصيل' },
      { code: 'TransactionAssignmentsCreate', label: 'إنشاء الإحالات' },
      { code: 'TransactionResponsesEdit', label: 'تعديل الردود' },
      { code: 'TransactionAttachmentsManage', label: 'إدارة المرفقات' },
    ],
  },
  {
    title: 'التقارير',
    permissions: [
      { code: 'ReportsView', label: 'عرض' },
      { code: 'ReportsBuild', label: 'إنشاء تقرير' },
      { code: 'ReportsExportPdf', label: 'تصدير PDF' },
      { code: 'ReportsExportExcel', label: 'تصدير Excel' },
      { code: 'ReportsTemplatesManage', label: 'إدارة القوالب' },
    ],
  },
  {
    title: 'طباعة التعقيب',
    permissions: [
      { code: 'FollowUpPrintView', label: 'عرض' },
      { code: 'FollowUpPrintCreate', label: 'إنشاء' },
      { code: 'FollowUpPrintExport', label: 'تصدير/طباعة' },
    ],
  },
  {
    title: 'البيانات المرجعية',
    permissions: [
      { code: 'LookupsView', label: 'عرض' },
      { code: 'LookupsManage', label: 'إدارة' },
    ],
  },
  {
    title: 'المستخدمون والصلاحيات',
    permissions: [
      { code: 'UsersView', label: 'عرض المستخدمين' },
      { code: 'UsersManage', label: 'إدارة المستخدمين' },
      { code: 'UserPermissionsManage', label: 'إدارة الصلاحيات' },
    ],
  },
  {
    title: 'إعدادات النظام',
    permissions: [
      { code: 'SystemSettingsView', label: 'عرض' },
      { code: 'SystemSettingsManage', label: 'إدارة' },
    ],
  },
];

export const knownPermissions = new Set<PermissionCode>(
  permissionGroups.flatMap((group) => group.permissions.map((permission) => permission.code)),
);

export function keepKnownPermissions(values: readonly string[]): PermissionCode[] {
  return values.filter((value): value is PermissionCode =>
    knownPermissions.has(value as PermissionCode));
}
