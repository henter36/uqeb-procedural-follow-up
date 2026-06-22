import { useState } from 'react';
import { Outlet } from 'react-router-dom';
import Sidebar from './Sidebar';
import TopBar from './TopBar';

export default function AppLayout() {
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <div className="app-shell">
      <Sidebar mobileOpen={mobileOpen} onMobileClose={() => setMobileOpen(false)} />
      <div className="app-main-area">
        {mobileOpen && (
          <button
            type="button"
            className="mobile-sidebar-backdrop"
            aria-label="إغلاق القائمة"
            onClick={() => setMobileOpen(false)}
            onKeyDown={(e) => { if (e.key === 'Escape') setMobileOpen(false); }}
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
