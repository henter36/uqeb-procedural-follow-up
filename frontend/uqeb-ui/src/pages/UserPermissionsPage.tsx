import { useEffect, useMemo, useState } from 'react';
import { usersApi } from '../api/services';
import type { User } from '../api/types';
import type { PermissionCode } from '../auth/permissions';
import { PageHeader } from '../components/ui';

type PermissionEntry = [PermissionCode, string];

const permissionGroups: { title: string; permissions: PermissionEntry[] }[] = [
  {
    title: 'لوحة المتابعة',
    permissions: [['DashboardView', 'عرض']],
  },
  {
    title: 'المعاملات',
    permissions: [
      ['TransactionsView', 'عرض'],
      ['TransactionsCreate', 'إنشاء'],
      ['TransactionsEdit', 'تعديل'],
      ['TransactionsCancel', 'إلغاء/إغلاق'],
      ['TransactionsExport', 'تصدير'],
      ['TransactionDetailsView', 'عرض التفاصيل'],
      ['TransactionAssignmentsCreate', 'إنشاء الإحالات'],
      ['TransactionResponsesEdit', 'تعديل الردود'],
      ['TransactionAttachmentsManage', 'إدارة المرفقات'],
    ],
  },
  {
    title: 'التقارير',
    permissions: [
      ['ReportsView', 'عرض'],
      ['ReportsBuild', 'إنشاء تقرير'],
      ['ReportsExportPdf', 'تصدير PDF'],
      ['ReportsExportExcel', 'تصدير Excel'],
      ['ReportsTemplatesManage', 'إدارة القوالب'],
    ],
  },
  {
    title: 'طباعة التعقيب',
    permissions: [
      ['FollowUpPrintView', 'عرض'],
      ['FollowUpPrintCreate', 'إنشاء'],
      ['FollowUpPrintExport', 'تصدير/طباعة'],
    ],
  },
  {
    title: 'البيانات المرجعية',
    permissions: [
      ['LookupsView', 'عرض'],
      ['LookupsManage', 'إدارة'],
    ],
  },
  {
    title: 'المستخدمون والصلاحيات',
    permissions: [
      ['UsersView', 'عرض المستخدمين'],
      ['UsersManage', 'إدارة المستخدمين'],
      ['UserPermissionsManage', 'إدارة الصلاحيات'],
    ],
  },
  {
    title: 'إعدادات النظام',
    permissions: [
      ['SystemSettingsView', 'عرض'],
      ['SystemSettingsManage', 'إدارة'],
    ],
  },
];

const knownPermissions = new Set<PermissionCode>(
  permissionGroups.flatMap((group) => group.permissions.map(([permission]) => permission)),
);

function keepKnownPermissions(values: readonly string[]): PermissionCode[] {
  return values.filter((value): value is PermissionCode =>
    knownPermissions.has(value as PermissionCode));
}

export default function UserPermissionsPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [selectedUserId, setSelectedUserId] = useState<number | null>(null);
  const [selectedPermissions, setSelectedPermissions] = useState<Set<PermissionCode>>(new Set());
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const selectedUser = useMemo(
    () => users.find((user) => user.id === selectedUserId) ?? null,
    [users, selectedUserId],
  );

  useEffect(() => {
    let active = true;
    usersApi.getAll()
      .then(({ data }) => {
        if (!active) return;
        setUsers(data);
        setSelectedUserId(data[0]?.id ?? null);
      })
      .catch(() => {
        if (active) setError('تعذر تحميل المستخدمين.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (!selectedUserId) return;

    let active = true;
    usersApi.getPermissions(selectedUserId)
      .then(({ data }) => {
        if (!active) return;
        setSelectedPermissions(new Set(keepKnownPermissions(data)));
        setError(null);
        setSaved(false);
      })
      .catch(() => {
        if (active) setError('تعذر تحميل صلاحيات المستخدم.');
      });

    return () => {
      active = false;
    };
  }, [selectedUserId]);

  const togglePermission = (permission: PermissionCode) => {
    setSaved(false);
    setSelectedPermissions((current) => {
      const next = new Set(current);
      if (next.has(permission)) next.delete(permission);
      else next.add(permission);
      return next;
    });
  };

  const save = async () => {
    if (!selectedUserId) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      await usersApi.replacePermissions(selectedUserId, keepKnownPermissions([...selectedPermissions]));
      setSaved(true);
    } catch {
      setError('تعذر حفظ صلاحيات المستخدم.');
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <div className="loading">جاري تحميل المستخدمين...</div>;

  return (
    <div>
      <PageHeader title="صلاحيات المستخدمين" />

      {error && <div className="alert alert-error mb-3">{error}</div>}
      {saved && <div className="alert alert-success mb-3">تم حفظ الصلاحيات.</div>}

      <div className="card mb-4">
        <label htmlFor="permission-user">المستخدم</label>
        <select
          id="permission-user"
          value={selectedUserId ?? ''}
          onChange={(event) => setSelectedUserId(Number(event.target.value))}
        >
          {users.map((user) => (
            <option key={user.id} value={user.id}>
              {user.fullName} ({user.username}) - {user.role}
            </option>
          ))}
        </select>
        {selectedUser?.role === 'Admin' && (
          <p className="text-muted mt-2">حساب المدير يحتفظ بكل الصلاحيات تلقائيًا.</p>
        )}
      </div>

      <div className="grid gap-4">
        {permissionGroups.map((group) => (
          <section key={group.title} className="card">
            <h3 className="card-title">{group.title}</h3>
            <div className="checkbox-grid">
              {group.permissions.map(([permission, label]) => (
                <label key={permission} className="checkbox-row">
                  <input
                    type="checkbox"
                    checked={selectedPermissions.has(permission)}
                    onChange={() => togglePermission(permission)}
                  />
                  <span>{label}</span>
                </label>
              ))}
            </div>
          </section>
        ))}
      </div>

      <div className="page-actions mt-4">
        <button type="button" className="btn btn-primary" onClick={save} disabled={!selectedUserId || saving}>
          {saving ? 'جاري الحفظ...' : 'حفظ الصلاحيات'}
        </button>
      </div>
    </div>
  );
}
