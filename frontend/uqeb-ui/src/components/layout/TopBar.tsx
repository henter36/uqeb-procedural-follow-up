import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/useAuth';
import { roleLabels } from '../../utils/labels';
import { getRouteMeta } from './navConfig';
import { usePendingPrintSummary } from '../../hooks/usePendingPrintSummary';
import Breadcrumbs from '../ui/Breadcrumbs';
import { IconMenu } from '../ui/icons';

type TopBarProps = Readonly<{
  onMenuToggle: () => void;
  mobileOpen?: boolean;
}>;

function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/);
  if (parts.length >= 2) return parts[0][0] + parts[1][0];
  return name.slice(0, 2);
}

export default function TopBar({ onMenuToggle, mobileOpen = false }: TopBarProps) {
  const { user, logout, canClose } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const meta = getRouteMeta(location.pathname, location.search);
  const { pendingTotal } = usePendingPrintSummary(canClose);

  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  return (
    <header className="app-topbar">
      <div className="topbar-start">
        <button
          type="button"
          className="btn btn-ghost btn-sm mobile-menu-btn"
          onClick={onMenuToggle}
          aria-label="فتح القائمة"
          aria-expanded={mobileOpen}
        >
          <IconMenu aria-hidden="true" />
        </button>
        <h1 className="topbar-title">{meta.title}</h1>
        <Breadcrumbs items={meta.breadcrumbs} />
      </div>

      <div className="topbar-end">
        {canClose && pendingTotal > 0 && (
          <Link to="/follow-up-print/pending" className="topbar-pending-badge" title="خطابات بانتظار التسجيل">
            <span className="badge badge-orange">{pendingTotal}</span>
            <span className="topbar-pending-label">بانتظار التسجيل</span>
          </Link>
        )}
        {user?.departmentName && (
          <span className="text-muted topbar-dept">{user.departmentName}</span>
        )}
        <div className="topbar-user">
          <div className="user-avatar" aria-hidden="true">
            {getInitials(user?.fullName ?? '?')}
          </div>
          <div>
            <div className="topbar-user-name">{user?.fullName}</div>
            <div className="topbar-user-role">{roleLabels[user?.role ?? ''] ?? user?.role}</div>
          </div>
        </div>
        <button type="button" className="btn btn-outline btn-sm" onClick={handleLogout}>
          خروج
        </button>
      </div>
    </header>
  );
}
