import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import ReplyFormPanel from './ReplyFormPanel';
import { addDaysIso, todayLocalIso } from '../../utils/localDate';

afterEach(() => {
  cleanup();
});

describe('ReplyFormPanel', () => {
  it('starts department completion date empty and requires it before submit', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();

    render(
      <ReplyFormPanel
        title="تسجيل إفادة الإدارة"
        dateLabel="تاريخ إنجاز الإدارة"
        dateRequiredMessage="تاريخ إنجاز الإدارة مطلوب."
        summaryLabel="ملخص الإفادة *"
        submitLabel="حفظ الإفادة"
        onDirtyChange={vi.fn()}
        onSubmit={onSubmit}
        onSuccess={vi.fn()}
        onCancel={vi.fn()}
      />,
    );

    expect(screen.getByLabelText('تاريخ إنجاز الإدارة *')).toHaveValue('');

    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'تمت الإفادة');
    await user.click(screen.getByRole('button', { name: 'حفظ الإفادة' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('تاريخ إنجاز الإدارة مطلوب.');
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('submits the manually entered department completion date', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    const onSuccess = vi.fn();

    render(
      <ReplyFormPanel
        title="تسجيل إفادة الإدارة"
        dateLabel="تاريخ إنجاز الإدارة"
        dateRequiredMessage="تاريخ إنجاز الإدارة مطلوب."
        summaryLabel="ملخص الإفادة *"
        submitLabel="حفظ الإفادة"
        onDirtyChange={vi.fn()}
        onSubmit={onSubmit}
        onSuccess={onSuccess}
        onCancel={vi.fn()}
      />,
    );

    await user.type(screen.getByLabelText('تاريخ إنجاز الإدارة *'), '16/01/1448');
    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'تمت الإفادة');
    await user.click(screen.getByRole('button', { name: 'حفظ الإفادة' }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledWith({
      replyDate: '2026-07-01T00:00:00',
      replySummary: 'تمت الإفادة',
    }));
    expect(onSuccess).toHaveBeenCalled();
  });

  it('rejects a future department completion date', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue(undefined);

    render(
      <ReplyFormPanel
        title="تسجيل إفادة الإدارة"
        dateLabel="تاريخ إنجاز الإدارة"
        dateRequiredMessage="تاريخ إنجاز الإدارة مطلوب."
        summaryLabel="ملخص الإفادة *"
        submitLabel="حفظ الإفادة"
        onDirtyChange={vi.fn()}
        onSubmit={onSubmit}
        onSuccess={vi.fn()}
        onCancel={vi.fn()}
      />,
    );

    fireEvent.change(screen.getByLabelText('تاريخ إنجاز الإدارة - اختيار من التقويم'), {
      target: { value: addDaysIso(todayLocalIso(), 1) },
    });
    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'تمت الإفادة');
    await user.click(screen.getByRole('button', { name: 'حفظ الإفادة' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('لا يمكن أن يكون التاريخ بعد تاريخ اليوم.');
    expect(onSubmit).not.toHaveBeenCalled();
  });
});
