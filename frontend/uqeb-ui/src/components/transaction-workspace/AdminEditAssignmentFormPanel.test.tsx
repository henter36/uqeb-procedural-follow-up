import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import AdminEditAssignmentFormPanel from './AdminEditAssignmentFormPanel';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    adminEditAssignment: vi.fn(),
  },
}));

afterEach(() => {
  cleanup();
});

describe('AdminEditAssignmentFormPanel', () => {
  const baseAssignment = {
    id: 10,
    departmentId: 1,
    departmentName: 'إدارة اختبار',
    assignedDate: '2026-01-02',
    requiredAction: 'مراجعة',
    replyDueDays: 5,
    dueDate: '2026-01-07',
    replyStatus: 'Pending',
    requiresReply: true,
    status: 'Open',
    isOverdue: false,
    canAdminEdit: true,
    createdByName: 'مدير النظام',
    createdAt: '2026-01-02',
  };

  it('renders current assignment values for admin editing', () => {
    render(
      <AdminEditAssignmentFormPanel
        transactionId={1}
        assignmentId={10}
        initialAssignment={{
          ...baseAssignment,
          letterNumber: 'L-1',
        }}
        fallbackLetterNumber="OUT-1"
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    expect(screen.getByLabelText('رقم الخطاب')).toHaveValue('L-1');
    expect(screen.getByLabelText('الإجراء المطلوب')).toHaveValue('مراجعة');
    expect(screen.getByLabelText('عدد أيام الرد')).toHaveValue(5);
    expect(screen.getByRole('button', { name: 'حفظ التعديلات' })).toBeInTheDocument();
  });

  it('uses outgoing number fallback only when assignment letter number is missing', () => {
    const { unmount } = render(
      <AdminEditAssignmentFormPanel
        transactionId={1}
        assignmentId={10}
        initialAssignment={{ ...baseAssignment, letterNumber: undefined }}
        fallbackLetterNumber="OUT-1"
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    expect(screen.getByLabelText('رقم الخطاب')).toHaveValue('OUT-1');
    unmount();

    render(
      <AdminEditAssignmentFormPanel
        transactionId={1}
        assignmentId={10}
        initialAssignment={{ ...baseAssignment, letterNumber: 'L-2' }}
        fallbackLetterNumber="OUT-1"
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    expect(screen.getByLabelText('رقم الخطاب')).toHaveValue('L-2');
  });
});
