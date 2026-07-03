import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import HijriDateInput from './HijriDateInput';
import { addDaysIso, todayLocalIso } from '../utils/localDate';

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

  it('normalizes pasted year-first Hijri dates into day month year display', () => {
    const onChange = vi.fn();
    render(
      <HijriDateInput
        id="date"
        label="تاريخ المعاملة"
        value=""
        onChange={onChange}
      />,
    );

    fireEvent.change(screen.getByLabelText('تاريخ المعاملة'), {
      target: { value: '1447/01/01', selectionStart: 10, selectionEnd: 10 },
      nativeEvent: { inputType: 'insertFromPaste' },
    });

    const input = screen.getByLabelText('تاريخ المعاملة');
    expect(input).toHaveValue('01/01/1447');
    expect(onChange).not.toHaveBeenCalledWith('');
  });

  it('keeps mid-field edits stable and normalizes on blur', () => {
    render(
      <HijriDateInput
        id="date"
        label="تاريخ المعاملة"
        value=""
        onChange={vi.fn()}
      />,
    );

    const input = screen.getByLabelText('تاريخ المعاملة');
    fireEvent.change(input, {
      target: { value: '12/3/1448', selectionStart: 4, selectionEnd: 4 },
      nativeEvent: { inputType: 'deleteContentBackward' },
    });

    expect(input).toHaveValue('12/3/1448');

    fireEvent.blur(input);

    expect(input).toHaveValue('12/03/1448');
  });

  it('rejects future dates only when disallowFutureDate is enabled', () => {
    const blockedChange = vi.fn();
    const allowedChange = vi.fn();
    const futureDate = addDaysIso(todayLocalIso(), 1);

    const { rerender } = render(
      <HijriDateInput
        id="date"
        label="تاريخ الإحالة"
        value=""
        onChange={blockedChange}
        disallowFutureDate
      />,
    );

    fireEvent.change(screen.getByLabelText('تاريخ الإحالة - اختيار من التقويم'), { target: { value: futureDate } });

    expect(blockedChange).toHaveBeenLastCalledWith(futureDate);
    expect(screen.getByText('لا يمكن أن يكون التاريخ بعد تاريخ اليوم.')).toBeInTheDocument();

    rerender(
      <HijriDateInput
        id="date"
        label="تاريخ الاستحقاق"
        value=""
        onChange={allowedChange}
      />,
    );

    fireEvent.change(screen.getByLabelText('تاريخ الاستحقاق - اختيار من التقويم'), { target: { value: futureDate } });

    expect(allowedChange).toHaveBeenLastCalledWith(futureDate);
    expect(screen.queryByText('لا يمكن أن يكون التاريخ بعد تاريخ اليوم.')).not.toBeInTheDocument();
  });
});
