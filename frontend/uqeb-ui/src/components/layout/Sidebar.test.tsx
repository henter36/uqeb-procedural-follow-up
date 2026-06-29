import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import Sidebar from './Sidebar';
import * as safeStorage from '../../utils/safeStorage';

const { mockUseAuth } = vi.hoisted(() => ({
  mockUseAuth: vi.fn(),
}));

vi.mock('../../context/useAuth', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('../../utils/safeStorage', () => ({
  getStorageItem: vi.fn(() => null),
  setStorageItem: vi.fn(() => true),
}));

function createAdminAuthState() {
  return {
    isAdmin: true,
    canClose: true,
    canOperateFollowUpPrint: true,
    user: { fullName: 'مختبر', role: 'Admin' },
    logout: vi.fn(),
    login: vi.fn(),
    canEdit: true,
    isDepartmentUser: false,
    canReviewDepartmentResponse: true,
  };
}

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
    vi.unstubAllEnvs();

    mockUseAuth.mockReset();
    mockUseAuth.mockReturnValue(createAdminAuthState());
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllEnvs();
  });

  it('shows report builder link for authorized admin by default', () => {
    renderSidebar();
    expect(screen.getByRole('link', { name: 'منشئ التقارير' })).toBeInTheDocument();
  });

  it('hides report builder link for non-admin users', () => {
    mockUseAuth.mockReturnValue({
      ...createAdminAuthState(),
      isAdmin: false,
      user: {
        fullName: 'مشرف',
        role: 'Supervisor',
      },
    });

    renderSidebar();
    expect(screen.queryByRole('link', { name: 'منشئ التقارير' })).not.toBeInTheDocument();
  });

  it('hides report builder link when feature flag is explicitly false', () => {
    vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'false');
    renderSidebar();
    expect(screen.queryByRole('link', { name: 'منشئ التقارير' })).not.toBeInTheDocument();
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

  it('hides privileged workflow links for department users', () => {
    mockUseAuth.mockReturnValue({
      ...createAdminAuthState(),
      isAdmin: false,
      canClose: false,
      canOperateFollowUpPrint: false,
      canEdit: false,
      isDepartmentUser: true,
      canReviewDepartmentResponse: false,
      user: {
        fullName: 'موظف إدارة',
        role: 'DepartmentUser',
      },
    });

    renderSidebar();

    expect(screen.getByRole('link', { name: 'معاملات إدارتي' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'المعاملات' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'التقارير' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'التحويلات والردود' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'بانتظار تسجيل التعقيب' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'مهام طباعة التعقيب' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'إفادات بانتظار المراجعة' })).not.toBeInTheDocument();
  });
});
