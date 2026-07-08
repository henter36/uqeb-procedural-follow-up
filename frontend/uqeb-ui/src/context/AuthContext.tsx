import { useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import { authApi } from '../api/services';
import type { LoginResponse } from '../api/types';
import type { PermissionCode } from '../auth/permissions';
import { AuthContext } from './auth-context';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<LoginResponse | null>(() => {
    const stored = localStorage.getItem('user');
    return stored ? JSON.parse(stored) : null;
  });
  const token = user?.token;
  const [permissions, setPermissions] = useState<PermissionCode[]>([]);

  useEffect(() => {
    if (user) localStorage.setItem('user', JSON.stringify(user));
    else localStorage.removeItem('user');
  }, [user]);

  useEffect(() => {
    if (!token || !localStorage.getItem('token')) return;

    let active = true;
    authApi.getMyPermissions()
      .then(({ data }) => {
        if (!active) return;
        const nextPermissions = data as PermissionCode[];
        setPermissions(nextPermissions);
        setUser((current) => current ? { ...current, permissions: nextPermissions } : current);
      })
      .catch(() => {
        if (active) setPermissions([]);
      });

    return () => {
      active = false;
    };
  }, [token]);

  const login = (u: LoginResponse) => {
    localStorage.setItem('token', u.token);
    setPermissions((u.permissions ?? []) as PermissionCode[]);
    setUser(u);
  };

  const logout = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    setPermissions([]);
    setUser(null);
  };

  const role = user?.role ?? '';
  const hasPermission = (permission: PermissionCode) =>
    role === 'Admin' || permissions.includes(permission);
  const canOperateFollowUpPrint = hasPermission('FollowUpPrintView');
  const canReviewDepartmentResponse = hasPermission('TransactionResponsesEdit');
  return (
    <AuthContext.Provider value={{
      user, login, logout,
      permissions,
      hasPermission,
      isAdmin: role === 'Admin',
      canEdit: hasPermission('TransactionsEdit'),
      canClose: hasPermission('TransactionsCancel'),
      canOperateFollowUpPrint,
      isDepartmentUser: role === 'DepartmentUser',
      canReviewDepartmentResponse,
    }}>
      {children}
    </AuthContext.Provider>
  );
}
