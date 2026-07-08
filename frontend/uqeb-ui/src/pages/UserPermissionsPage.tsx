import { useEffect, useMemo, useState } from 'react';
import { usersApi } from '../api/services';
import type { User } from '../api/types';
import type { PermissionCode } from '../auth/permissions';
import { keepKnownPermissions, permissionGroups } from '../auth/permissionGroups';
import { PageHeader } from '../components/ui';

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
              {group.permissions.map(({ code, label }) => (
                <label key={code} className="checkbox-row">
                  <input
                    type="checkbox"
                    checked={selectedPermissions.has(code)}
                    onChange={() => togglePermission(code)}
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
