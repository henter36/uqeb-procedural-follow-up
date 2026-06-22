import { describe, expect, it, vi, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import AppLayout from './AppLayout';

vi.mock('./Sidebar', () => ({
  default: ({ mobileOpen, onMobileClose }: { mobileOpen: boolean; onMobileClose: () => void }) => (
    <div data-testid="sidebar" data-mobile-open={String(mobileOpen)}>
      {mobileOpen && (
        <button type="button" onClick={onMobileClose}>إغلاق القائمة</button>
      )}
    </div>
  ),
}));

vi.mock('./TopBar', () => ({
  default: ({ onMenuToggle }: { onMenuToggle: () => void }) => (
    <button type="button" onClick={onMenuToggle}>فتح القائمة</button>
  ),
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    Outlet: () => <div>محتوى</div>,
  };
});

describe('AppLayout', () => {
  afterEach(() => {
    cleanup();
  });

  it('closes mobile sidebar on Escape from anywhere', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AppLayout />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole('button', { name: 'فتح القائمة' }));
    expect(screen.getByTestId('sidebar')).toHaveAttribute('data-mobile-open', 'true');

    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.getByTestId('sidebar')).toHaveAttribute('data-mobile-open', 'false');
    });
  });
});
