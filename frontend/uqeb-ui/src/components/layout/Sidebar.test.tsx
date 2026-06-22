import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import Sidebar from './Sidebar';
import * as safeStorage from '../../utils/safeStorage';

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({
    isAdmin: true,
    canClose: true,
    user: { fullName: 'مختبر', role: 'Admin' },
    logout: vi.fn(),
    login: vi.fn(),
    canEdit: true,
    isDepartmentUser: false,
  }),
}));

vi.mock('../../utils/safeStorage', () => ({
  getStorageItem: vi.fn(() => null),
  setStorageItem: vi.fn(() => true),
}));

function renderSidebar(mobileOpen = false, onMobileClose = vi.fn()) {
  return render(
    <MemoryRouter initialEntries={['/transactions']}>
      <Sidebar mobileOpen={mobileOpen} onMobileClose={onMobileClose} />
    </MemoryRouter>,
  );
}

describe('Sidebar', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('marks the active link using pathname and search', () => {
    render(
      <MemoryRouter initialEntries={['/reports?tab=waiting']}>
        <Sidebar mobileOpen={false} onMobileClose={vi.fn()} />
      </MemoryRouter>,
    );

    const waitingLink = screen.getByRole('link', { name: 'التحويلات والردود' });
    expect(waitingLink).toHaveAttribute('aria-current', 'page');

    const reportsLink = screen.getByRole('link', { name: 'التقارير' });
    expect(reportsLink).not.toHaveAttribute('aria-current');
  });

  it('survives localStorage read failure on mount', () => {
    vi.mocked(safeStorage.getStorageItem).mockReturnValue(null);
    expect(() => renderSidebar()).not.toThrow();
  });

  it('survives localStorage write failure when collapsing', async () => {
    vi.mocked(safeStorage.setStorageItem).mockReturnValue(false);
    const user = userEvent.setup();
    renderSidebar();

    await user.click(screen.getByRole('button', { name: 'طي القائمة الجانبية' }));
    expect(safeStorage.setStorageItem).toHaveBeenCalled();
  });

  it('shows mobile close button when sidebar is open', () => {
    renderSidebar(true);
    expect(screen.getByRole('button', { name: 'إغلاق القائمة' })).toBeInTheDocument();
  });
});
