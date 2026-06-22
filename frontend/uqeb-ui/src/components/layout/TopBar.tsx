import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { roleLabels } from '../../utils/labels';
import { getRouteMeta } from './navConfig';
import Breadcrumbs from '../ui/Breadcrumbs';
import { IconMenu } from '../ui/icons';

type TopBarProps = {
  onMenuToggle: () => void;
};

function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/);
  if (parts.length >= 2) return parts[0][0] + parts[1][0];
  return name.slice(0, 2);
}

export default function TopBar({ onMenuToggle }: TopBarProps) {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const meta = getRouteMeta(location.pathname, location.search);

  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  return (
    <header className="app-topbar">
      <div className="topbar-start">
        <button
          type="button"
          className="btn btn-ghost btn-sm"
          onClick={onMenuToggle}
          aria-label="فتح القائمة"
          style={{ display: 'none' }}
          id="mobile-menu-btn"
        >
          <IconMenu />
        </button>
        <h1 className="topbar-title">{meta.title}</h1>
        <Breadcrumbs items={meta.breadcrumbs} />
      </div>

      <div className="topbar-end">
        {user?.departmentName && (
          <span className="text-muted" style={{ fontSize: '0.8rem' }}>
            {user.departmentName}
          </span>
        )}
        <div className="topbar-user">
          <div className="user-avatar" aria-hidden="true">
            {getInitials(user?.fullName ?? '?')}
          </div>
          <div>
            <div style={{ fontWeight: 500, color: 'var(--color-text)' }}>{user?.fullName}</div>
            <div style={{ fontSize: '0.75rem' }}>{roleLabels[user?.role ?? ''] ?? user?.role}</div>
          </div>
        </div>
        <button type="button" className="btn btn-outline btn-sm" onClick={handleLogout}>
          خروج
        </button>
      </div>
    </header>
  );
}
