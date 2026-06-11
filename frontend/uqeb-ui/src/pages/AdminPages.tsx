import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { usersApi, departmentsApi, externalPartiesApi } from '../api/services';
import type { User, Department, ExternalParty } from '../api/types';
import { roleLabels } from '../utils/labels';

export function UsersPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({ username: '', password: '', fullName: '', email: '', role: 'DataEntry', departmentId: '' });

  const load = () => { usersApi.getAll().then((r) => setUsers(r.data)); };
  useEffect(() => { load(); departmentsApi.getAll().then((r) => setDepartments(r.data)); }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    await usersApi.create({ ...form, departmentId: form.departmentId ? +form.departmentId : null });
    setShowForm(false);
    load();
  };

  return (
    <div>
      <div className="page-header">
        <h2 className="page-title">إدارة المستخدمين</h2>
        <button className="btn btn-primary" onClick={() => setShowForm(true)}>إضافة مستخدم</button>
      </div>
      <table className="data-table">
        <thead><tr><th>اسم المستخدم</th><th>الاسم</th><th>الدور</th><th>الإدارة</th><th>الحالة</th></tr></thead>
        <tbody>
          {users.map((u) => (
            <tr key={u.id}>
              <td>{u.username}</td>
              <td>{u.fullName}</td>
              <td>{roleLabels[u.role] || u.role}</td>
              <td>{u.departmentName || '-'}</td>
              <td>{u.isActive ? 'نشط' : 'معطل'}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {showForm && (
        <div className="modal-overlay"><div className="modal">
          <h3>إضافة مستخدم</h3>
          <form onSubmit={submit}>
            <div className="form-group"><label>اسم المستخدم</label><input required value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} /></div>
            <div className="form-group"><label>كلمة المرور</label><input type="password" required value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} /></div>
            <div className="form-group"><label>الاسم الكامل</label><input required value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} /></div>
            <div className="form-group"><label>الدور</label>
              <select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })}>
                {Object.entries(roleLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
              </select>
            </div>
            <div className="form-group"><label>الإدارة</label>
              <select value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value })}>
                <option value="">بدون</option>
                {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select>
            </div>
            <div className="form-actions"><button type="submit" className="btn btn-primary">حفظ</button>
              <button type="button" className="btn btn-outline" onClick={() => setShowForm(false)}>إلغاء</button></div>
          </form>
        </div></div>
      )}
    </div>
  );
}

export function DepartmentsPage() {
  const [items, setItems] = useState<Department[]>([]);
  const [form, setForm] = useState({ name: '', code: '' });
  const [showForm, setShowForm] = useState(false);

  const load = () => departmentsApi.getAll(false).then((r) => setItems(r.data));
  useEffect(() => { load(); }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    await departmentsApi.create(form);
    setShowForm(false);
    setForm({ name: '', code: '' });
    load();
  };

  return (
    <div>
      <div className="page-header">
        <h2 className="page-title">إدارة الإدارات</h2>
        <button className="btn btn-primary" onClick={() => setShowForm(true)}>إضافة إدارة</button>
      </div>
      <table className="data-table">
        <thead><tr><th>الاسم</th><th>الرمز</th><th>الحالة</th></tr></thead>
        <tbody>{items.map((d) => <tr key={d.id}><td>{d.name}</td><td>{d.code || '-'}</td><td>{d.isActive ? 'نشط' : 'معطل'}</td></tr>)}</tbody>
      </table>
      {showForm && (
        <div className="modal-overlay"><div className="modal">
          <h3>إضافة إدارة</h3>
          <form onSubmit={submit}>
            <div className="form-group"><label>الاسم</label><input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></div>
            <div className="form-group"><label>الرمز</label><input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} /></div>
            <div className="form-actions"><button type="submit" className="btn btn-primary">حفظ</button>
              <button type="button" className="btn btn-outline" onClick={() => setShowForm(false)}>إلغاء</button></div>
          </form>
        </div></div>
      )}
    </div>
  );
}

export function ExternalPartiesPage() {
  const [items, setItems] = useState<ExternalParty[]>([]);
  const [form, setForm] = useState({ name: '', type: '', contactInfo: '' });
  const [showForm, setShowForm] = useState(false);

  const load = () => externalPartiesApi.getAll(false).then((r) => setItems(r.data));
  useEffect(() => { load(); }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    await externalPartiesApi.create(form);
    setShowForm(false);
    setForm({ name: '', type: '', contactInfo: '' });
    load();
  };

  return (
    <div>
      <div className="page-header">
        <h2 className="page-title">الجهات الخارجية</h2>
        <button className="btn btn-primary" onClick={() => setShowForm(true)}>إضافة جهة</button>
      </div>
      <table className="data-table">
        <thead><tr><th>الاسم</th><th>النوع</th><th>معلومات الاتصال</th><th>الحالة</th></tr></thead>
        <tbody>{items.map((p) => <tr key={p.id}><td>{p.name}</td><td>{p.type || '-'}</td><td>{p.contactInfo || '-'}</td><td>{p.isActive ? 'نشط' : 'معطل'}</td></tr>)}</tbody>
      </table>
      {showForm && (
        <div className="modal-overlay"><div className="modal">
          <h3>إضافة جهة خارجية</h3>
          <form onSubmit={submit}>
            <div className="form-group"><label>الاسم</label><input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></div>
            <div className="form-group"><label>النوع</label><input value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value })} /></div>
            <div className="form-group"><label>معلومات الاتصال</label><input value={form.contactInfo} onChange={(e) => setForm({ ...form, contactInfo: e.target.value })} /></div>
            <div className="form-actions"><button type="submit" className="btn btn-primary">حفظ</button>
              <button type="button" className="btn btn-outline" onClick={() => setShowForm(false)}>إلغاء</button></div>
          </form>
        </div></div>
      )}
    </div>
  );
}
