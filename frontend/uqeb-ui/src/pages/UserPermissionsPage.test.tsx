import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import UserPermissionsPage from './UserPermissionsPage';
import { usersApi } from '../api/services';

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
    } as never);
    vi.mocked(usersApi.getPermissions).mockResolvedValue({ data: ['ReportsView'] } as never);
    vi.mocked(usersApi.replacePermissions).mockResolvedValue({} as never);
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
});
