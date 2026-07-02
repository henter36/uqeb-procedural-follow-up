import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import AdminEditResponseFormPanel from './AdminEditResponseFormPanel';
import * as services from '../../api/services';
import type { DepartmentResponseDto } from '../../api/types';

vi.mock('../../api/services', () => ({
  departmentResponsesApi: {
    getById: vi.fn(),
    adminEdit: vi.fn(),
  },
}));

const response: DepartmentResponseDto = {
  id: 100,
  transactionId: 1,
  transactionSubject: 'موضوع',
  internalTrackingNumber: 'TRK-1',
  departmentId: 1,
  departmentName: 'إدارة اختبار',
  responseText: 'نص الرد الحالي',
  status: 'SubmittedForReview',
  submittedByName: 'موظف إدارة',
  submittedAt: '2026-01-10T00:00:00Z',
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
    vi.mocked(services.departmentResponsesApi.getById).mockResolvedValue({ data: response } as never);

    renderPanel();

    expect(await screen.findByDisplayValue('نص الرد الحالي')).toBeInTheDocument();
    expect(screen.getByLabelText('تاريخ إنجاز الإدارة')).toHaveValue('1447/07/21');
    expect(services.departmentResponsesApi.getById).toHaveBeenCalledWith(100);
  });

  it('AdminEditResponseFormPanel_Shows_Loading_While_Fetching', () => {
    vi.mocked(services.departmentResponsesApi.getById).mockReturnValue(new Promise(() => {}) as never);

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
});
