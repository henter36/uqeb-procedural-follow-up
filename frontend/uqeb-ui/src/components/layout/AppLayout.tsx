import { useState, useEffect } from 'react';
import { Outlet } from 'react-router-dom';
import Sidebar from './Sidebar';
import TopBar from './TopBar';

export default function AppLayout() {
  const [mobileOpen, setMobileOpen] = useState(false);

  useEffect(() => {
    if (!mobileOpen) return undefined;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setMobileOpen(false);
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [mobileOpen]);

  const closeMobile = () => setMobileOpen(false);

  return (
    <div className="app-shell">
      <Sidebar mobileOpen={mobileOpen} onMobileClose={closeMobile} />
      <div className="app-main-area">
        {mobileOpen && (
          <div
            className="mobile-sidebar-backdrop"
            aria-hidden="true"
            onClick={closeMobile}
          />
        )}
        <TopBar mobileOpen={mobileOpen} onMenuToggle={() => setMobileOpen((o) => !o)} />
        <main className="app-content" id="main-content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
