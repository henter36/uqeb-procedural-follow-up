import { useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import type { LoginResponse } from '../api/types';
import { AuthContext } from './auth-context';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<LoginResponse | null>(() => {
    const stored = localStorage.getItem('user');
    return stored ? JSON.parse(stored) : null;
  });

  useEffect(() => {
    if (user) localStorage.setItem('user', JSON.stringify(user));
    else localStorage.removeItem('user');
  }, [user]);

  const login = (u: LoginResponse) => {
    localStorage.setItem('token', u.token);
    setUser(u);
  };

  const logout = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    setUser(null);
  };

  const role = user?.role ?? '';
  return (
    <AuthContext.Provider value={{
      user, login, logout,
      isAdmin: role === 'Admin',
      canEdit: ['Admin', 'Supervisor', 'DataEntry'].includes(role),
      canClose: ['Admin', 'Supervisor'].includes(role),
      isDepartmentUser: role === 'DepartmentUser',
    }}>
      {children}
    </AuthContext.Provider>
  );
}
