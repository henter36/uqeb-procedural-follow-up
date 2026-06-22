import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { isAxiosError } from 'axios';
import {
  usersApi, departmentsApi, externalPartiesApi, categoriesApi,
} from '../api/services';
import type { User, Department, ExternalParty, Category } from '../api/types';
import { roleLabels } from '../utils/labels';
import SearchableSelect, { type SelectOption } from '../components/SearchableSelect';
import { ReferenceDataPage } from '../components/ReferenceDataPage';
import { FormModal } from '../components/ReferenceDataFormModal';
import { FieldError } from '../components/ReferenceDataFieldError';
import { StatusBadge } from '../components/ReferenceDataStatusBadge';
import type { ReferenceListParams } from '../components/referenceDataTypes';

function apiError(err: unknown, fallback: string) {
  return isAxiosError(err) ? (err.response?.data as { message?: string })?.message ?? fallback : fallback;
}

function DepartmentForm({
  editing, onClose, onSaved,
}: {
  editing: Department | null;
  onClose: () => void;
  onSaved: (item: Department) => void;
  listParams: ReferenceListParams;
}) {
  const [name, setName] = useState(editing?.name ?? '');
  const [code, setCode] = useState(editing?.code ?? '');
  const [isActive, setIsActive] = useState(editing?.isActive ?? true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setFieldErrors({});
    const trimmed = name.trim().replace(/\s+/g, ' ');
    if (!trimmed) {
      setFieldErrors({ name: 'الاسم مطلوب' });
      return;
    }
    setSubmitting(true);
    try {
      const payload = { name: trimmed, code: code.trim() || null, isActive };
      const res = editing
        ? await departmentsApi.update(editing.id, payload)
        : await departmentsApi.create(payload);
      onSaved(res.data);
    } catch (err) {
      setError(apiError(err, 'تعذر حفظ الإدارة'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <FormModal
      title={editing ? 'تعديل إدارة' : 'إضافة إدارة'}
      onClose={onClose}
      onSubmit={submit}
      submitting={submitting}
      submitLabel={editing ? 'حفظ التعديلات' : 'إضافة'}
    >
      <div className="form-group">
        <label>الاسم</label>
        <input value={name} onChange={(e) => setName(e.target.value)} required />
        <FieldError message={fieldErrors.name} />
      </div>
      <div className="form-group">
        <label>الرمز</label>
        <input value={code} onChange={(e) => setCode(e.target.value)} />
      </div>
      {editing && (
        <div className="form-group">
          <label><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> نشطة</label>
        </div>
      )}
      {error && <div className="alert alert-error">{error}</div>}
    </FormModal>
  );
}

export function DepartmentsPage() {
  return (
    <ReferenceDataPage<Department>
      title="إدارة الإدارات"
      addLabel="إضافة إدارة"
      fetchPage={(p) => departmentsApi.search(p)}
      getRowId={(d) => d.id}
      columns={[
        { key: 'name', label: 'الاسم', sortable: true, render: (d) => d.name },
        { key: 'code', label: 'الرمز', sortable: true, render: (d) => d.code || '-' },
        { key: 'isActive', label: 'الحالة', sortable: true, render: (d) => <StatusBadge active={d.isActive} /> },
        { key: 'createdAt', label: 'تاريخ الإضافة', sortable: true, render: (d) => d.createdAt ? new Date(d.createdAt).toLocaleDateString('ar-SA') : '-' },
      ]}
      onDeactivate={(d) => departmentsApi.update(d.id, { isActive: false }).then(() => undefined)}
      canDeactivate={(d) => d.isActive}
      deactivateLabel="تعطيل"
      renderForm={({ editing, onClose, onSaved, listParams }) => (
        <DepartmentForm editing={editing} onClose={onClose} onSaved={onSaved} listParams={listParams} />
      )}
    />
  );
}

function ExternalPartyForm({
  editing, onClose, onSaved,
}: {
  editing: ExternalParty | null;
  onClose: () => void;
  onSaved: (item: ExternalParty) => void;
  listParams: ReferenceListParams;
}) {
  const [name, setName] = useState(editing?.name ?? '');
  const [type, setType] = useState(editing?.type ?? '');
  const [contactInfo, setContactInfo] = useState(editing?.contactInfo ?? '');
  const [isActive, setIsActive] = useState(editing?.isActive ?? true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    const trimmed = name.trim().replace(/\s+/g, ' ');
    if (!trimmed) return;
    setSubmitting(true);
    try {
      const payload = { name: trimmed, type: type.trim() || null, contactInfo: contactInfo.trim() || null, isActive };
      const res = editing
        ? await externalPartiesApi.update(editing.id, payload)
        : await externalPartiesApi.create(payload);
      onSaved(res.data);
    } catch (err) {
      setError(apiError(err, 'تعذر حفظ الجهة'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <FormModal title={editing ? 'تعديل جهة خارجية' : 'إضافة جهة خارجية'} onClose={onClose} onSubmit={submit} submitting={submitting} submitLabel={editing ? 'حفظ التعديلات' : 'إضافة'}>
      <div className="form-group"><label>الاسم</label><input value={name} onChange={(e) => setName(e.target.value)} required /></div>
      <div className="form-group"><label>النوع</label><input value={type} onChange={(e) => setType(e.target.value)} /></div>
      <div className="form-group"><label>معلومات الاتصال</label><input value={contactInfo} onChange={(e) => setContactInfo(e.target.value)} /></div>
      {editing && <div className="form-group"><label><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> نشطة</label></div>}
      {error && <div className="alert alert-error">{error}</div>}
    </FormModal>
  );
}

export function ExternalPartiesPage() {
  return (
    <ReferenceDataPage<ExternalParty>
      title="إدارة الجهات الخارجية"
      addLabel="إضافة جهة"
      fetchPage={(p) => externalPartiesApi.search(p)}
      getRowId={(p) => p.id}
      columns={[
        { key: 'name', label: 'الاسم', sortable: true, render: (p) => p.name },
        { key: 'type', label: 'النوع', sortable: true, render: (p) => p.type || '-' },
        { key: 'contactInfo', label: 'الاتصال', render: (p) => p.contactInfo || '-' },
        { key: 'isActive', label: 'الحالة', sortable: true, render: (p) => <StatusBadge active={p.isActive} /> },
      ]}
      onDeactivate={(p) => externalPartiesApi.update(p.id, { isActive: false }).then(() => undefined)}
      canDeactivate={(p) => p.isActive}
      renderForm={({ editing, onClose, onSaved, listParams }) => (
        <ExternalPartyForm editing={editing} onClose={onClose} onSaved={onSaved} listParams={listParams} />
      )}
    />
  );
}

function CategoryForm({
  editing, onClose, onSaved,
}: {
  editing: Category | null;
  onClose: () => void;
  onSaved: (item: Category) => void;
  listParams: ReferenceListParams;
}) {
  const [name, setName] = useState(editing?.name ?? '');
  const [code, setCode] = useState(editing?.code ?? '');
  const [isActive, setIsActive] = useState(editing?.isActive ?? true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    const trimmed = name.trim().replace(/\s+/g, ' ');
    if (!trimmed) return;
    setSubmitting(true);
    try {
      const payload = { name: trimmed, code: code.trim() || null, isActive };
      const res = editing
        ? await categoriesApi.update(editing.id, payload)
        : await categoriesApi.create(payload);
      onSaved(res.data);
    } catch (err) {
      setError(apiError(err, 'تعذر حفظ التصنيف'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <FormModal title={editing ? 'تعديل تصنيف' : 'إضافة تصنيف'} onClose={onClose} onSubmit={submit} submitting={submitting} submitLabel={editing ? 'حفظ التعديلات' : 'إضافة'}>
      <div className="form-group"><label>الاسم</label><input value={name} onChange={(e) => setName(e.target.value)} required /></div>
      <div className="form-group"><label>الرمز</label><input value={code} onChange={(e) => setCode(e.target.value)} /></div>
      {editing && <div className="form-group"><label><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> نشط</label></div>}
      {error && <div className="alert alert-error">{error}</div>}
    </FormModal>
  );
}

export function CategoriesPage() {
  return (
    <ReferenceDataPage<Category>
      title="إدارة التصنيفات"
      addLabel="إضافة تصنيف"
      fetchPage={(p) => categoriesApi.search(p)}
      getRowId={(c) => c.id}
      columns={[
        { key: 'name', label: 'الاسم', sortable: true, render: (c) => c.name },
        { key: 'code', label: 'الرمز', sortable: true, render: (c) => c.code || '-' },
        { key: 'isActive', label: 'الحالة', sortable: true, render: (c) => <StatusBadge active={c.isActive} /> },
      ]}
      onDeactivate={(c) => categoriesApi.update(c.id, { isActive: false }).then(() => undefined)}
      canDeactivate={(c) => c.isActive}
      renderForm={({ editing, onClose, onSaved, listParams }) => (
        <CategoryForm editing={editing} onClose={onClose} onSaved={onSaved} listParams={listParams} />
      )}
    />
  );
}

function UserForm({
  editing, onClose, onSaved,
}: {
  editing: User | null;
  onClose: () => void;
  onSaved: (item: User) => void;
  listParams: ReferenceListParams;
}) {
  const [departments, setDepartments] = useState<SelectOption[]>([]);
  const [username, setUsername] = useState(editing?.username ?? '');
  const [password, setPassword] = useState('');
  const [fullName, setFullName] = useState(editing?.fullName ?? '');
  const [email, setEmail] = useState(editing?.email ?? '');
  const [role, setRole] = useState(editing?.role ?? 'DataEntry');
  const [departmentId, setDepartmentId] = useState<number | ''>(editing?.departmentId ?? '');
  const [isActive, setIsActive] = useState(editing?.isActive ?? true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showReset, setShowReset] = useState(false);
  const [newPassword, setNewPassword] = useState('');

  useEffect(() => {
    departmentsApi.getAll(false).then((r) => {
      setDepartments(r.data.map((d) => ({ id: d.id, name: d.name, isActive: d.isActive, subLabel: d.code })));
    });
  }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      if (editing) {
        const res = await usersApi.update(editing.id, {
          username: username.trim(),
          fullName: fullName.trim(),
          email: email.trim() || null,
          role,
          departmentId: departmentId === '' ? null : departmentId,
          isActive,
        });
        onSaved(res.data);
      } else {
        const res = await usersApi.create({
          username: username.trim(),
          password,
          fullName: fullName.trim(),
          email: email.trim() || null,
          role,
          departmentId: departmentId === '' ? null : departmentId,
        });
        onSaved(res.data);
      }
    } catch (err) {
      setError(apiError(err, 'تعذر حفظ المستخدم'));
    } finally {
      setSubmitting(false);
    }
  };

  const resetPassword = async () => {
    if (!editing || !newPassword) return;
    setSubmitting(true);
    setError(null);
    try {
      await usersApi.resetPassword(editing.id, newPassword);
      setShowReset(false);
      setNewPassword('');
      setError(null);
      alert('تم إعادة تعيين كلمة المرور');
    } catch (err) {
      setError(apiError(err, 'تعذر إعادة تعيين كلمة المرور'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <FormModal title={editing ? 'تعديل مستخدم' : 'إضافة مستخدم'} onClose={onClose} onSubmit={submit} submitting={submitting} submitLabel={editing ? 'حفظ التعديلات' : 'إضافة'}>
      <div className="form-group"><label>اسم المستخدم</label><input required value={username} onChange={(e) => setUsername(e.target.value)} /></div>
      {!editing && <div className="form-group"><label>كلمة المرور</label><input type="password" required value={password} onChange={(e) => setPassword(e.target.value)} /></div>}
      <div className="form-group"><label>الاسم الكامل</label><input required value={fullName} onChange={(e) => setFullName(e.target.value)} /></div>
      <div className="form-group"><label>البريد الإلكتروني</label><input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></div>
      <div className="form-group"><label>الدور</label>
        <select value={role} onChange={(e) => setRole(e.target.value)}>
          {Object.entries(roleLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
      </div>
      <SearchableSelect label="الإدارة" value={departmentId} onChange={setDepartmentId} options={departments} allowClear />
      {editing && <div className="form-group"><label><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> نشط</label></div>}
      {editing && (
        <div className="form-group">
          {!showReset ? (
            <button type="button" className="btn btn-outline" onClick={() => setShowReset(true)}>إعادة تعيين كلمة المرور</button>
          ) : (
            <>
              <label>كلمة المرور الجديدة</label>
              <input type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
              <button type="button" className="btn btn-secondary" onClick={resetPassword} disabled={submitting || !newPassword}>تأكيد إعادة التعيين</button>
            </>
          )}
        </div>
      )}
      {error && <div className="alert alert-error">{error}</div>}
    </FormModal>
  );
}

export function UsersPage() {
  return (
    <ReferenceDataPage<User>
      title="إدارة المستخدمين"
      addLabel="إضافة مستخدم"
      fetchPage={(p) => usersApi.search({ ...p, sortBy: p.sortBy === 'name' ? 'fullName' : p.sortBy })}
      getRowId={(u) => u.id}
      columns={[
        { key: 'username', label: 'اسم المستخدم', sortable: true, render: (u) => u.username },
        { key: 'fullName', label: 'الاسم', sortable: true, render: (u) => u.fullName },
        { key: 'email', label: 'البريد', render: (u) => u.email || '-' },
        { key: 'role', label: 'الدور', render: (u) => roleLabels[u.role] || u.role },
        { key: 'department', label: 'الإدارة', sortable: true, render: (u) => u.departmentName || '-' },
        { key: 'isActive', label: 'الحالة', sortable: true, render: (u) => <StatusBadge active={u.isActive} /> },
      ]}
      onDeactivate={(u) => usersApi.update(u.id, { isActive: false }).then(() => undefined)}
      canDeactivate={(u) => u.isActive}
      renderForm={({ editing, onClose, onSaved, listParams }) => (
        <UserForm editing={editing} onClose={onClose} onSaved={onSaved} listParams={listParams} />
      )}
    />
  );
}
