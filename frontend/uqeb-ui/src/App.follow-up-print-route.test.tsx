import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { Outlet } from 'react-router-dom';
import App from './App';
import { authApi } from './api/services';

vi.mock('./api/services', async (importOriginal) => ({
  ...await importOriginal<typeof import('./api/services')>(),
  authApi: {
    getMyPermissions: vi.fn(),
  },
}));

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

vi.mock('./pages/FollowUpPrintEligiblePage', () => ({
  default: () => <div>eligible-print-screen</div>,
}));

vi.mock('./pages/FollowUpPrintJobsPage', () => ({
  default: () => <div>print-jobs-screen</div>,
}));

vi.mock('./pages/TransactionsList', () => ({
  default: () => <div>transactions-screen</div>,
}));

vi.mock('./pages/Dashboard', () => ({
  default: () => <div>dashboard-screen</div>,
}));

function setUser(role: string) {
  localStorage.setItem('token', 'token');
  localStorage.setItem('user', JSON.stringify({
    token: 'token',
    username: role.toLowerCase(),
    fullName: role,
    role,
  }));
}

describe('App follow-up print route permissions', () => {
  beforeEach(() => {
    localStorage.clear();
    window.history.pushState({}, '', '/follow-up-print/pending');
    setUser('DataEntry');
    vi.mocked(authApi.getMyPermissions).mockResolvedValue({
      data: ['FollowUpPrintView'],
    } as unknown as Awaited<ReturnType<typeof authApi.getMyPermissions>>);
  });

  afterEach(() => {
    cleanup();
    localStorage.clear();
  });

  it('allows DataEntry users to open the pending follow-up print screen', async () => {
    render(<App />);

    expect(await screen.findByText('pending-screen')).toBeInTheDocument();
  });

  it.each([
    ['/transactions', 'transactions-screen'],
    ['/follow-up-print/eligible', 'eligible-print-screen'],
    ['/follow-up-print/jobs', 'print-jobs-screen'],
    ['/follow-up-print/pending', 'pending-screen'],
  ])('blocks DepartmentUser from %s', async (path, blockedText) => {
    window.history.pushState({}, '', path);
    localStorage.clear();
    setUser('DepartmentUser');
    vi.mocked(authApi.getMyPermissions).mockResolvedValue({
      data: ['TransactionResponsesEdit'],
    } as unknown as Awaited<ReturnType<typeof authApi.getMyPermissions>>);

    render(<App />);

    expect(screen.queryByText(blockedText)).not.toBeInTheDocument();
    expect(await screen.findByText('ليست لديك صلاحية الوصول لهذه الشاشة.')).toBeInTheDocument();
  });
});
