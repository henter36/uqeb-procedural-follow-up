import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import AdminEditAssignmentFormPanel from './AdminEditAssignmentFormPanel';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    adminEditAssignment: vi.fn(),
  },
}));

describe('AdminEditAssignmentFormPanel', () => {
  it('renders current assignment values for admin editing', () => {
    render(
      <AdminEditAssignmentFormPanel
        transactionId={1}
        assignmentId={10}
        initialAssignment={{
          id: 10,
          departmentId: 1,
          departmentName: 'إدارة اختبار',
          letterNumber: 'L-1',
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
        }}
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
});
