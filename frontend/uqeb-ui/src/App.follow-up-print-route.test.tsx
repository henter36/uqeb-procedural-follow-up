import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { Outlet } from 'react-router-dom';
import App from './App';

vi.mock('./components/Layout', () => ({
  default: () => <Outlet />,
}));

vi.mock('./context/ReferenceDataProvider', () => ({
  ReferenceDataProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

vi.mock('./context/PendingPrintSummaryContext', () => ({
  PendingPrintSummaryProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

vi.mock('./pages/FollowUpPrintPendingPage', () => ({
  default: () => <div>pending-screen</div>,
}));

describe('App follow-up print route permissions', () => {
  beforeEach(() => {
    localStorage.clear();
    window.history.pushState({}, '', '/follow-up-print/pending');
    localStorage.setItem('token', 'token');
    localStorage.setItem('user', JSON.stringify({
      token: 'token',
      username: 'data-entry',
      fullName: 'مدخل بيانات',
      role: 'DataEntry',
    }));
  });

  afterEach(() => {
    cleanup();
    localStorage.clear();
  });

  it('allows DataEntry users to open the pending follow-up print screen', () => {
    render(<App />);

    expect(screen.getByText('pending-screen')).toBeInTheDocument();
  });
});
