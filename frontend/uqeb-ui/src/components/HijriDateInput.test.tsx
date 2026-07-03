import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import HijriDateInput from './HijriDateInput';

describe('HijriDateInput', () => {
  it('formats typed digits as day month year and ignores letters', async () => {
    const user = userEvent.setup();
    render(
      <HijriDateInput
        id="date"
        label="تاريخ المعاملة"
        value=""
        onChange={vi.fn()}
      />,
    );

    const input = screen.getByLabelText('تاريخ المعاملة');
    await user.type(input, 'ab01011447cd');

    expect(input).toHaveValue('01/01/1447');
    expect(input).toHaveAttribute('dir', 'ltr');
    expect(input).toHaveClass('hijri-date-text-input');
  });

  it('exposes a calendar picker while keeping manual input available', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <HijriDateInput
        id="date"
        label="تاريخ إنجاز الإدارة"
        value=""
        onChange={onChange}
      />,
    );

    const manualInput = screen.getByLabelText('تاريخ إنجاز الإدارة');
    const calendarInput = screen.getByLabelText('تاريخ إنجاز الإدارة - اختيار من التقويم');

    await user.type(manualInput, '01011447');
    expect(manualInput).toHaveValue('01/01/1447');

    fireEvent.change(calendarInput, { target: { value: '2026-07-01' } });

    expect(onChange).toHaveBeenLastCalledWith('2026-07-01');
    expect((manualInput as HTMLInputElement).value).toMatch(/^\d{2}\/\d{2}\/\d{4}$/);
  });
});
