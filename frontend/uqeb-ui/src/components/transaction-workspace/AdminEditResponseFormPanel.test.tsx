import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AxiosResponse } from 'axios';
import AdminEditResponseFormPanel from './AdminEditResponseFormPanel';
import * as services from '../../api/services';
import type { DepartmentResponseDto } from '../../api/types';

vi.mock('../../api/services', () => ({
  departmentResponsesApi: {
    getById: vi.fn(),
    adminEdit: vi.fn(),
  },
}));

function mockAxiosResponse<T>(data: T): AxiosResponse<T> {
  return {
    data,
    status: 200,
    statusText: 'OK',
    headers: {},
    config: { headers: {} } as AxiosResponse<T>['config'],
  };
}

const response: DepartmentResponseDto = {
  id: 100,
  transactionId: 1,
  transactionSubject: 'موضوع',
  internalTrackingNumber: 'TRK-1',
  departmentId: 1,
  departmentName: 'إدارة اختبار',
  responseText: 'نص الرد الحالي',
  responseDate: '2026-01-10T00:00:00Z',
  status: 'SubmittedForReview',
  submittedByName: 'موظف إدارة',
  submittedAt: '2026-01-12T00:00:00Z',
  createdAt: '2026-01-01T00:00:00Z',
  attachments: [],
};

function renderPanel(initialResponse?: DepartmentResponseDto) {
  return render(
    <AdminEditResponseFormPanel
      responseId={100}
      initialResponse={initialResponse}
      onDirtyChange={vi.fn()}
      onCancel={vi.fn()}
      onSuccess={vi.fn()}
    />,
  );
}

describe('AdminEditResponseFormPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('AdminEditResponseFormPanel_Loads_Response_When_InitialResponse_Not_Provided', async () => {
    vi.mocked(services.departmentResponsesApi.getById).mockResolvedValue(mockAxiosResponse(response));

    renderPanel();

    expect(await screen.findByDisplayValue('نص الرد الحالي')).toBeInTheDocument();
    expect(screen.getByLabelText('تاريخ إنجاز الإدارة')).toHaveValue('21/07/1447');
    expect(services.departmentResponsesApi.getById).toHaveBeenCalledWith(100);
  });

  it('AdminEditResponseFormPanel_Shows_Loading_While_Fetching', () => {
    vi.mocked(services.departmentResponsesApi.getById).mockReturnValue(new Promise<AxiosResponse<DepartmentResponseDto>>(() => {}));

    renderPanel();

    expect(screen.getByRole('status')).toHaveTextContent('جاري تحميل بيانات الرد...');
    expect(screen.queryByLabelText('نص الرد')).not.toBeInTheDocument();
  });

  it('AdminEditResponseFormPanel_Shows_Error_When_Fetch_Fails', async () => {
    vi.mocked(services.departmentResponsesApi.getById).mockRejectedValue(new Error('تعذر تحميل الرد'));

    renderPanel();

    await waitFor(() => expect(screen.getByText('تعذر تحميل الرد')).toBeInTheDocument());
    expect(screen.queryByLabelText('نص الرد')).not.toBeInTheDocument();
  });

  it('sends the corrected value as responseDate, not submittedAt', async () => {
    vi.mocked(services.departmentResponsesApi.adminEdit).mockResolvedValue(mockAxiosResponse(response));
    const user = userEvent.setup();

    renderPanel(response);

    const dateField = screen.getByLabelText('تاريخ إنجاز الإدارة');
    await user.clear(dateField);
    await user.type(dateField, '05/07/1447');
    await user.type(screen.getByLabelText('سبب التعديل *'), 'تصحيح تاريخ الإنجاز');
    await user.click(screen.getByRole('button', { name: 'حفظ التصحيح' }));

    await waitFor(() => expect(services.departmentResponsesApi.adminEdit).toHaveBeenCalledTimes(1));
    const payload = vi.mocked(services.departmentResponsesApi.adminEdit).mock.calls[0][1] as Record<string, unknown>;
    expect(payload.responseDate).not.toBe('2026-01-10');
    expect(payload).not.toHaveProperty('submittedAt');
  });
});
