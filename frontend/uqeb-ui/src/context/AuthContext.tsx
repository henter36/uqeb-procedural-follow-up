import { createContext, useContext, useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import type { LoginResponse } from '../api/types';

interface AuthContextType {
  user: LoginResponse | null;
  login: (user: LoginResponse) => void;
  logout: () => void;
  isAdmin: boolean;
  canEdit: boolean;
  canClose: boolean;
  isDepartmentUser: boolean;
}

const AuthContext = createContext<AuthContextType | null>(null);

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

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
