import { useState, useEffect } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/useAuth';
import { APP_DISPLAY_NAME, APP_SUBTITLE } from '../../constants/app';
import { isNavActive, buildNavSections, type NavItem } from './navConfig';
import { IconChevron } from '../ui/icons';
import { getStorageItem, setStorageItem } from '../../utils/safeStorage';
import { usePendingPrintSummary } from '../../hooks/usePendingPrintSummary';

const SIDEBAR_KEY = 'uqeb-sidebar-collapsed';

type SidebarProps = Readonly<{
  mobileOpen: boolean;
  onMobileClose: () => void;
}>;

export default function Sidebar({ mobileOpen, onMobileClose }: SidebarProps) {
  const { isAdmin, canClose, canOperateFollowUpPrint, isDepartmentUser, canReviewDepartmentResponse } = useAuth();
  const location = useLocation();
  const [collapsed, setCollapsed] = useState(() => getStorageItem(SIDEBAR_KEY) === 'true');
  const { pendingTotal } = usePendingPrintSummary(canOperateFollowUpPrint);

  useEffect(() => {
    setStorageItem(SIDEBAR_KEY, String(collapsed));
  }, [collapsed]);

  const isVisible = (item: NavItem) => {
    if (item.adminOnly && !isAdmin) return false;
    if (item.supervisorOnly && !canClose) return false;
    if (item.followUpPrintOnly && !canOperateFollowUpPrint) return false;
    if (item.departmentUserOnly && !isDepartmentUser) return false;
    if (item.departmentResponseReviewOnly && !canReviewDepartmentResponse) return false;
    return true;
  };

  const handleNavClick = () => {
    if (mobileOpen) onMobileClose();
  };

  const navSections = buildNavSections();

  return (
    <aside
      className={`app-sidebar${collapsed ? ' collapsed' : ''}${mobileOpen ? ' mobile-open' : ''}`}
      aria-label="القائمة الجانبية"
    >
      <div className="sidebar-brand">
        {mobileOpen && (
          <button
            type="button"
            className="sidebar-mobile-close"
            onClick={onMobileClose}
            aria-label="إغلاق القائمة"
          >
            ✕
          </button>
        )}
        <div className="sidebar-brand-icon" aria-hidden="true">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2" />
            <rect x="9" y="3" width="6" height="4" rx="1" />
          </svg>
        </div>
        <div className="sidebar-brand-text">
          <h1>{APP_DISPLAY_NAME}</h1>
          <span>{APP_SUBTITLE}</span>
        </div>
      </div>

      <nav className="sidebar-nav">
        {navSections.map((section) => {
          const visibleItems = section.items.filter(isVisible);
          if (visibleItems.length === 0) return null;

          return (
            <div key={section.label}>
              <div className="sidebar-section-label">{section.label}</div>
              {visibleItems.map((item) => {
                const Icon = item.icon;
                const active = isNavActive(item, location.pathname, location.search);
                return (
                  <Link
                    key={item.path}
                    to={item.path}
                    className={`sidebar-link${active ? ' active' : ''}`}
                    data-tooltip={item.label}
                    aria-current={active ? 'page' : undefined}
                    onClick={handleNavClick}
                  >
                    <span className="sidebar-link-icon" aria-hidden="true">
                      <Icon width={18} height={18} />
                    </span>
                    <span className="sidebar-link-label">{item.label}</span>
                    {item.badgeKey === 'pendingPrints' && pendingTotal > 0 && (
                      <span className="sidebar-link-badge badge badge-orange">{pendingTotal}</span>
                    )}
                  </Link>
                );
              })}
            </div>
          );
        })}
      </nav>

      <div className="sidebar-footer">
        <button
          type="button"
          className="sidebar-toggle"
          onClick={() => setCollapsed((c) => !c)}
          aria-label={collapsed ? 'توسيع القائمة الجانبية' : 'طي القائمة الجانبية'}
          aria-expanded={!collapsed}
        >
          <IconChevron direction={collapsed ? 'left' : 'right'} width={18} height={18} />
        </button>
      </div>
    </aside>
  );
}
