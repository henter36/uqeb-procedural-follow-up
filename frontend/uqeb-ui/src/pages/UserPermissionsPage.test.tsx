import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import UserPermissionsPage from './UserPermissionsPage';
import { usersApi } from '../api/services';
import { keepKnownPermissions } from '../auth/permissionGroups';

type UsersResponse = Awaited<ReturnType<typeof usersApi.getAll>>;
type PermissionsResponse = Awaited<ReturnType<typeof usersApi.getPermissions>>;
type ReplacePermissionsResponse = Awaited<ReturnType<typeof usersApi.replacePermissions>>;

vi.mock('../api/services', () => ({
  usersApi: {
    getAll: vi.fn(),
    getPermissions: vi.fn(),
    replacePermissions: vi.fn(),
  },
}));

describe('UserPermissionsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(usersApi.getAll).mockResolvedValue({
      data: [{
        id: 2,
        username: 'reader',
        fullName: 'قارئ',
        role: 'Reader',
        isActive: true,
      }],
    } as unknown as UsersResponse);
    vi.mocked(usersApi.getPermissions).mockResolvedValue({ data: ['ReportsView'] } as unknown as PermissionsResponse);
    vi.mocked(usersApi.replacePermissions).mockResolvedValue({} as unknown as ReplacePermissionsResponse);
  });

  afterEach(() => {
    cleanup();
  });

  it('saves selected permissions for the chosen user', async () => {
    const user = userEvent.setup();
    render(<UserPermissionsPage />);

    expect(await screen.findByLabelText('تصدير PDF')).not.toBeChecked();

    await user.click(screen.getByLabelText('تصدير PDF'));
    await user.click(screen.getByRole('button', { name: 'حفظ الصلاحيات' }));

    await waitFor(() => {
      expect(usersApi.replacePermissions).toHaveBeenCalledWith(
        2,
        expect.arrayContaining(['ReportsView', 'ReportsExportPdf']),
      );
    });
    expect(await screen.findByText('تم حفظ الصلاحيات.')).toBeInTheDocument();
  });

  it('does not save unknown permission values returned by the API', async () => {
    vi.mocked(usersApi.getPermissions).mockResolvedValue({
      data: ['ReportsExportPdf', 'NotAPermission'],
    } as unknown as PermissionsResponse);
    render(<UserPermissionsPage />);

    await waitFor(() => {
      expect(screen.getByLabelText('تصدير PDF')).toBeChecked();
    });

    await userEvent.click(screen.getByRole('button', { name: 'حفظ الصلاحيات' }));

    await waitFor(() => {
      expect(usersApi.replacePermissions).toHaveBeenCalledWith(
        2,
        expect.arrayContaining(['ReportsExportPdf']),
      );
      expect(usersApi.replacePermissions).toHaveBeenCalledWith(
        2,
        expect.not.arrayContaining(['NotAPermission']),
      );
    });
  });

  it('uses the shared permission catalog for validation', () => {
    expect(keepKnownPermissions(['ReportsView', 'NotAPermission'])).toEqual(['ReportsView']);
  });
});
