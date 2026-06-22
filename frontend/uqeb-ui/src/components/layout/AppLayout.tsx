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
        <TopBar onMenuToggle={() => setMobileOpen((o) => !o)} />
        <main className="app-content" id="main-content">
          <Outlet />
        </main>
      </div>
      {mobileOpen && (
        <button
          type="button"
          className="modal-overlay"
          style={{ zIndex: 150, background: 'rgba(0,0,0,0.3)', border: 'none', cursor: 'default' }}
          aria-label="إغلاق القائمة"
          onClick={() => setMobileOpen(false)}
          onKeyDown={(e) => { if (e.key === 'Escape') setMobileOpen(false); }}
        />
      )}
    </div>
  );
}
