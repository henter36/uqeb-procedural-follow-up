import { createContext } from 'react';
import type { LoginResponse } from '../api/types';
import type { PermissionCode } from '../auth/permissions';

export interface AuthContextType {
  user: LoginResponse | null;
  login: (user: LoginResponse) => void;
  logout: () => void;
  permissions: PermissionCode[];
  hasPermission: (permission: PermissionCode) => boolean;
  isAdmin: boolean;
  canEdit: boolean;
  canClose: boolean;
  canOperateFollowUpPrint: boolean;
  isDepartmentUser: boolean;
  canReviewDepartmentResponse: boolean;
}

export const AuthContext = createContext<AuthContextType | null>(null);
