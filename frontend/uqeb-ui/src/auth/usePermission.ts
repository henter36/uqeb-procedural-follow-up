import { useContext, useMemo } from 'react';
import { AuthContext } from '../context/auth-context';
import type { PermissionCode } from './permissions';

export function usePermission(permission: PermissionCode): boolean {
  const auth = useContext(AuthContext);

  return useMemo(() => {
    if (!auth) return false;
    if (auth.user?.role === 'Admin') return true;
    return auth.permissions?.includes(permission) ?? false;
  }, [auth, permission]);
}
