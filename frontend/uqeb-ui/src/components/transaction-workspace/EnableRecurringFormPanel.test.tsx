import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import EnableRecurringFormPanel from './EnableRecurringFormPanel';
import { transactionsApi } from '../../api/services';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    enableRecurring: vi.fn(),
  },
}));

afterEach(() => {
  cleanup();
});

describe('EnableRecurringFormPanel', () => {
  it('shows the expected first period end when an incoming date is present', () => {
    render(
      <EnableRecurringFormPanel
        transactionId={1}
        incomingDate="2026-01-10"
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    expect(screen.getByText('نهاية الفترة الأولى المتوقعة')).toBeInTheDocument();
    expect(screen.queryByText('—')).not.toBeInTheDocument();
  });

  it('does not crash and falls back to a placeholder when the incoming date is missing', () => {
    render(
      <EnableRecurringFormPanel
        transactionId={1}
        incomingDate=""
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    expect(screen.getByText('نهاية الفترة الأولى المتوقعة')).toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('does not send a dueDaysAfterPeriodEnd input and resets dirty state after success', async () => {
    const onDirtyChange = vi.fn();
    const onSuccess = vi.fn();
    vi.mocked(transactionsApi.enableRecurring).mockResolvedValue(
      { data: {} } as Awaited<ReturnType<typeof transactionsApi.enableRecurring>>,
    );

    const { container } = render(
      <EnableRecurringFormPanel
        transactionId={1}
        incomingDate="2026-01-10"
        onDirtyChange={onDirtyChange}
        onCancel={vi.fn()}
        onSuccess={onSuccess}
      />,
    );

    expect(container.querySelector('[name*="dueDays" i]')).toBeNull();

    const form = container.querySelector('form')!;
    fireEvent.submit(form);

    await waitFor(() => {
      expect(transactionsApi.enableRecurring).toHaveBeenCalledWith(1, expect.objectContaining({ dueDaysAfterPeriodEnd: 0 }));
    });
    await waitFor(() => {
      expect(onDirtyChange).toHaveBeenLastCalledWith(false);
    });
    expect(onSuccess).toHaveBeenCalled();
  });
});
