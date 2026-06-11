import { Link, Outlet, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { APP_DISPLAY_NAME, APP_SUBTITLE } from '../constants/app';

const statusLabels: Record<string, string> = {
  New: 'جديدة', InProgress: 'قيد الإجراء', Assigned: 'محالة',
  WaitingForReply: 'بانتظار رد', PartiallyReplied: 'رد جزئي',
  ReadyForResponse: 'جاهزة للإفادة', ResponseCompleted: 'تمت الإفادة',
  Closed: 'مغلقة', Overdue: 'متأخرة', Cancelled: 'ملغاة', Archived: 'مؤرشفة',
};

export { statusLabels };

export default function Layout() {
  const { user, logout, isAdmin } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  const navItems = [
    { path: '/', label: 'لوحة المتابعة' },
    { path: '/transactions', label: 'المعاملات' },
    { path: '/reports', label: 'التقارير' },
    ...(isAdmin ? [
      { path: '/users', label: 'المستخدمون' },
      { path: '/departments', label: 'الإدارات' },
      { path: '/external-parties', label: 'الجهات الخارجية' },
    ] : []),
  ];

  return (
    <div className="app-layout">
      <header className="app-header">
        <div className="header-brand">
          <h1>{APP_DISPLAY_NAME}</h1>
          <span className="subtitle">{APP_SUBTITLE}</span>
        </div>
        <nav className="main-nav">
          {navItems.map((item) => (
            <Link key={item.path} to={item.path}
              className={location.pathname === item.path || (item.path !== '/' && location.pathname.startsWith(item.path)) ? 'active' : ''}>
              {item.label}
            </Link>
          ))}
        </nav>
        <div className="header-user">
          <span>{user?.fullName} ({user?.role})</span>
          <button onClick={handleLogout} className="btn btn-outline">خروج</button>
        </div>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}
