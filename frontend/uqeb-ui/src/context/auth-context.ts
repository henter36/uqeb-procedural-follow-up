import { createContext } from 'react';
import type { LoginResponse } from '../api/types';

export interface AuthContextType {
  user: LoginResponse | null;
  login: (user: LoginResponse) => void;
  logout: () => void;
  isAdmin: boolean;
  canEdit: boolean;
  canClose: boolean;
  isDepartmentUser: boolean;
}

export const AuthContext = createContext<AuthContextType | null>(null);
