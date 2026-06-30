import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import AppVersionInfo from './AppVersionInfo';
import type { FrontendVersionInfo } from '../../version';
import type { SystemVersionInfo } from '../../api/types';

const frontendInfo: FrontendVersionInfo = {
  frontendVersion: '1.2.3',
  frontendCommitSha: 'abc1234',
  frontendBuildTimeUtc: '2026-06-30T10:30:00Z',
};

const backendInfo: SystemVersionInfo = {
  backendVersion: '20260630-103000',
  backendCommitSha: 'def5678',
  backendBuildTimeUtc: '2026-06-30T10:31:00Z',
  environment: 'Production',
};

describe('AppVersionInfo', () => {
  afterEach(() => {
    cleanup();
  });

  it('shows frontend version metadata', async () => {
    render(
      <AppVersionInfo
        frontendInfo={frontendInfo}
        loadBackendVersion={vi.fn().mockResolvedValue(backendInfo)}
        initiallyOpen
      />,
    );

    expect(screen.getByText(/الواجهة: v1\.2\.3/)).toBeInTheDocument();
    expect(screen.getByText(/commit abc1234/)).toBeInTheDocument();
  });

  it('shows backend version metadata when the request succeeds', async () => {
    render(
      <AppVersionInfo
        frontendInfo={frontendInfo}
        loadBackendVersion={vi.fn().mockResolvedValue(backendInfo)}
        initiallyOpen
      />,
    );

    expect(await screen.findByText(/الخادم: v20260630-103000/)).toBeInTheDocument();
    expect(screen.getByText(/commit def5678/)).toBeInTheDocument();
    expect(screen.getByText('البيئة: Production')).toBeInTheDocument();
  });

  it('handles backend version request failure without breaking the page', async () => {
    render(
      <AppVersionInfo
        frontendInfo={frontendInfo}
        loadBackendVersion={vi.fn().mockRejectedValue(new Error('network'))}
        initiallyOpen
      />,
    );

    expect(await screen.findByText('تعذر قراءة إصدار الخادم.')).toBeInTheDocument();
    expect(screen.getByText(/الواجهة: v1\.2\.3/)).toBeInTheDocument();
  });

  it('does not break when frontend metadata is missing', async () => {
    render(
      <AppVersionInfo
        frontendInfo={{ frontendVersion: '0.0.0', frontendCommitSha: 'local', frontendBuildTimeUtc: null }}
        loadBackendVersion={vi.fn().mockResolvedValue({ ...backendInfo, backendBuildTimeUtc: null })}
        initiallyOpen
      />,
    );

    expect(screen.getByText(/الواجهة: v0\.0\.0/)).toBeInTheDocument();
    expect(screen.getByText(/commit local/)).toBeInTheDocument();
    expect(screen.getAllByText(/غير متاح/).length).toBeGreaterThan(0);
    expect(await screen.findByText(/الخادم: v20260630-103000/)).toBeInTheDocument();
  });

  it('loads backend version only after the panel is opened', async () => {
    const loadBackendVersion = vi.fn().mockResolvedValue(backendInfo);
    const user = userEvent.setup();

    render(<AppVersionInfo frontendInfo={frontendInfo} loadBackendVersion={loadBackendVersion} />);

    expect(loadBackendVersion).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'معلومات الإصدار' }));

    await waitFor(() => expect(loadBackendVersion).toHaveBeenCalledOnce());
  });

  it('allows retrying backend version request after a failed attempt', async () => {
    const loadBackendVersion = vi.fn()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce(backendInfo);
    const user = userEvent.setup();

    render(<AppVersionInfo frontendInfo={frontendInfo} loadBackendVersion={loadBackendVersion} />);

    await user.click(screen.getByRole('button', { name: 'معلومات الإصدار' }));
    expect(await screen.findByText('تعذر قراءة إصدار الخادم.')).toBeInTheDocument();
    expect(loadBackendVersion).toHaveBeenCalledOnce();

    await user.click(screen.getByRole('button', { name: 'معلومات الإصدار' }));
    await user.click(screen.getByRole('button', { name: 'معلومات الإصدار' }));

    await waitFor(() => expect(loadBackendVersion).toHaveBeenCalledTimes(2));
    expect(await screen.findByText(/الخادم: v20260630-103000/)).toBeInTheDocument();
  });

  it('allows retrying after reopening the panel while the previous request is still pending', async () => {
    let resolveFirstRequest: (value: SystemVersionInfo) => void = () => undefined;
    const firstRequest = new Promise<SystemVersionInfo>((resolve) => {
      resolveFirstRequest = resolve;
    });
    const loadBackendVersion = vi.fn()
      .mockReturnValueOnce(firstRequest)
      .mockResolvedValueOnce(backendInfo);
    const user = userEvent.setup();

    render(<AppVersionInfo frontendInfo={frontendInfo} loadBackendVersion={loadBackendVersion} />);

    await user.click(screen.getByRole('button', { name: 'معلومات الإصدار' }));
    expect(screen.getByText('جاري قراءة إصدار الخادم...')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'معلومات الإصدار' }));
    await user.click(screen.getByRole('button', { name: 'معلومات الإصدار' }));

    await waitFor(() => expect(loadBackendVersion).toHaveBeenCalledTimes(2));
    resolveFirstRequest(backendInfo);
    expect(await screen.findByText(/الخادم: v20260630-103000/)).toBeInTheDocument();
  });
});
