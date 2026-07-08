import { Navigate } from 'react-router-dom';
import type { PermissionCode } from '../auth/permissions';
import { useAuth } from '../context/useAuth';

type ProtectedRouteProps = Readonly<{
  children: React.ReactNode;
  requiredRoles?: readonly string[];
  requiredPermission?: PermissionCode;
}>;

export default function ProtectedRoute({ children, requiredRoles, requiredPermission }: ProtectedRouteProps) {
  const auth = useAuth();
  const { user } = auth;
  const hasPermission = auth.hasPermission ?? ((permission: PermissionCode) =>
    user?.role === 'Admin' || auth.permissions?.includes(permission) || false);
  const token = localStorage.getItem('token');

  if (!user || !token) return <Navigate to="/login" replace />;

  const roleAllowed = !requiredRoles?.length || requiredRoles.includes(user.role);
  const permissionAllowed = !requiredPermission || hasPermission(requiredPermission);
  const allowed = requiredPermission ? permissionAllowed : roleAllowed;

  if (!requiredPermission && !allowed && requiredRoles?.length && !roleAllowed) {
    return <Navigate to="/" replace />;
  }

  if (!allowed) {
    return <div className="page-empty">ليست لديك صلاحية الوصول لهذه الشاشة.</div>;
  }

  return <>{children}</>;
}
