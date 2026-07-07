import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AxiosResponse } from 'axios';
import DepartmentResponseInlinePanel from './DepartmentResponseInlinePanel';
import * as services from '../../api/services';
import type { DepartmentResponseDto, DepartmentTransactionResponseItemDto } from '../../api/types';

vi.mock('../../api/services', () => ({
  departmentResponsesApi: {
    getById: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    submit: vi.fn(),
    uploadAttachment: vi.fn(),
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

function buildResponse(overrides: Partial<DepartmentResponseDto> = {}): DepartmentResponseDto {
  return {
    id: 1,
    transactionId: 1,
    transactionSubject: 'موضوع',
    internalTrackingNumber: 'TRK-1',
    departmentId: 5,
    departmentName: 'إدارة الاختبار',
    responseText: 'نص الإفادة',
    status: 'Draft',
    submittedByName: 'مستخدم',
    createdAt: '2026-01-01T00:00:00Z',
    attachments: [],
    ...overrides,
  };
}

describe('DepartmentResponseInlinePanel', () => {
  const onDirtyChange = vi.fn();
  const onMessage = vi.fn();
  const onCancel = vi.fn();
  const onChanged = vi.fn();

  function renderPanel(initialItem?: DepartmentTransactionResponseItemDto | null) {
    return render(
      <DepartmentResponseInlinePanel
        transactionId={1}
        initialItem={initialItem}
        onDirtyChange={onDirtyChange}
        onMessage={onMessage}
        onCancel={onCancel}
        onChanged={onChanged}
      />,
    );
  }

  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('opens with an empty completion date and does not default to today', async () => {
    renderPanel();
    expect(await screen.findByLabelText('تاريخ إنجاز الإدارة *')).toHaveValue('');
  });

  it('does not submit without a completion date', async () => {
    vi.mocked(services.departmentResponsesApi.create).mockResolvedValue(mockAxiosResponse(buildResponse()));
    vi.mocked(services.departmentResponsesApi.submit).mockResolvedValue(mockAxiosResponse(buildResponse({ status: 'SubmittedForReview' })));
    const user = userEvent.setup();
    renderPanel();

    await user.type(await screen.findByLabelText('نص الإفادة *'), 'نص الإفادة');
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    expect(await screen.findByText('تاريخ إنجاز الإدارة مطلوب قبل التقديم.')).toBeInTheDocument();
    expect(services.departmentResponsesApi.create).not.toHaveBeenCalled();
    expect(services.departmentResponsesApi.submit).not.toHaveBeenCalled();
  });

  it('sends the entered completion date, not left empty, when submitting', async () => {
    const created = buildResponse();
    vi.mocked(services.departmentResponsesApi.create).mockResolvedValue(mockAxiosResponse(created));
    vi.mocked(services.departmentResponsesApi.submit).mockResolvedValue(mockAxiosResponse(buildResponse({ status: 'SubmittedForReview' })));
    const user = userEvent.setup();
    renderPanel();

    await user.type(await screen.findByLabelText('نص الإفادة *'), 'نص الإفادة');
    await user.type(screen.getByLabelText('تاريخ إنجاز الإدارة *'), '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => {
      expect(services.departmentResponsesApi.create).toHaveBeenCalledWith(expect.objectContaining({
        transactionId: 1,
        responseDate: '2026-07-01T00:00:00',
      }));
    });
    await waitFor(() => expect(services.departmentResponsesApi.submit).toHaveBeenCalledWith(created.id));
  });

  it('loads the existing completion date when editing a returned-for-correction response', async () => {
    const existing = buildResponse({ id: 42, status: 'ReturnedForCorrection', responseDate: '2026-06-20T00:00:00Z' });
    vi.mocked(services.departmentResponsesApi.getById).mockResolvedValue(mockAxiosResponse(existing));

    renderPanel({
      transactionId: 1,
      internalTrackingNumber: 'TRK-1',
      subject: 'موضوع',
      priority: 'Normal',
      departmentId: 5,
      departmentName: 'إدارة الاختبار',
      departmentResponseId: 42,
      departmentResponseStatus: 'ReturnedForCorrection',
      canCreateResponse: false,
      canEditResponse: true,
      canSubmitResponse: true,
    });

    const dateField = await screen.findByLabelText('تاريخ إنجاز الإدارة *');
    await waitFor(() => expect(dateField).toHaveValue('05/01/1448'));
  });
});
