import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import MultiSelect from './MultiSelect';

const options = Array.from({ length: 11 }, (_, index) => ({
  id: index + 1,
  name: `إدارة ${index + 1}`,
  isActive: true,
}));

function renderMultiSelect(selected: readonly number[] = [], props: Partial<Parameters<typeof MultiSelect>[0]> = {}) {
  return render(
    <div>
      <button type="button">خارج القائمة</button>
      <MultiSelect
        options={options}
        selected={selected}
        onChange={vi.fn()}
        label="الإدارات"
        dataFieldName="departments"
        {...props}
      />
    </div>,
  );
}

describe('MultiSelect', () => {
  afterEach(() => {
    cleanup();
  });

  it.each([
    [[], 'لم يتم اختيار أي إدارة'],
    [[1], 'إدارة واحدة مختارة'],
    [[1, 2], 'إدارتان مختارتان'],
    [[1, 2, 3], '3 إدارات مختارة'],
    [[1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], '11 إدارة مختارة'],
  ] as const)('formats selected count for %s', (selected, label) => {
    renderMultiSelect(selected);

    expect(screen.getByRole('button', { name: label })).toBeInTheDocument();
  });

  it('MultiSelect_ClosesOnEscape', async () => {
    const user = userEvent.setup();
    renderMultiSelect();

    await user.click(screen.getByRole('button', { name: 'لم يتم اختيار أي إدارة' }));
    expect(screen.getByLabelText('إدارة 1')).toBeInTheDocument();

    await user.keyboard('{Escape}');

    expect(screen.queryByLabelText('إدارة 1')).not.toBeInTheDocument();
  });

  it('MultiSelect_ClosesOnOutsideClick', async () => {
    const user = userEvent.setup();
    renderMultiSelect();

    await user.click(screen.getByRole('button', { name: 'لم يتم اختيار أي إدارة' }));
    expect(screen.getByLabelText('إدارة 1')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'خارج القائمة' }));

    expect(screen.queryByLabelText('إدارة 1')).not.toBeInTheDocument();
  });

  it('MultiSelect_UsesInvalidClassAndDescribedByWithoutUnsupportedAriaInvalid', () => {
    renderMultiSelect([], {
      invalid: true,
      describedBy: 'departments-error',
    });

    const trigger = screen.getByRole('button', { name: 'لم يتم اختيار أي إدارة' });
    expect(trigger).toHaveClass('is-invalid');
    expect(trigger).toHaveAttribute('aria-describedby', 'departments-error');
    expect(trigger).toHaveAttribute('data-field-name', 'departments');
    expect(trigger).not.toHaveAttribute('aria-invalid');
    expect(trigger).not.toHaveAttribute('aria-haspopup');
  });
});
