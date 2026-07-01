import { cleanup, render, screen } from '@testing-library/react';
import type { FormEvent } from 'react';
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

    expect(screen.getByRole('button', { name: new RegExp(label) })).toBeInTheDocument();
  });

  it('closes on Escape', async () => {
    const user = userEvent.setup();
    renderMultiSelect();

    await user.click(screen.getByRole('button', { name: /لم يتم اختيار أي إدارة/ }));
    expect(screen.getByLabelText('إدارة 1')).toBeInTheDocument();

    await user.keyboard('{Escape}');

    expect(screen.queryByLabelText('إدارة 1')).not.toBeInTheDocument();
  });

  it('closes on outside click', async () => {
    const user = userEvent.setup();
    renderMultiSelect();

    await user.click(screen.getByRole('button', { name: /لم يتم اختيار أي إدارة/ }));
    expect(screen.getByLabelText('إدارة 1')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'خارج القائمة' }));

    expect(screen.queryByLabelText('إدارة 1')).not.toBeInTheDocument();
  });

  it('uses invalid class and describedBy without unsupported ARIA', () => {
    renderMultiSelect([], {
      invalid: true,
      describedBy: 'departments-error',
    });

    const trigger = screen.getByRole('button', { name: /لم يتم اختيار أي إدارة/ });
    expect(trigger).toHaveClass('is-invalid');
    expect(trigger).toHaveAttribute('aria-describedby', 'departments-error');
    expect(trigger).toHaveAttribute('data-field-name', 'departments');
    expect(trigger).not.toHaveAttribute('aria-invalid');
    expect(trigger).not.toHaveAttribute('aria-haspopup');
  });

  it('provides an accessible search field name', async () => {
    const user = userEvent.setup();
    renderMultiSelect();

    await user.click(screen.getByRole('button', { name: /لم يتم اختيار أي إدارة/ }));

    expect(screen.getByLabelText('بحث في الإدارات')).toBeInTheDocument();
  });

  it('supports configurable selected labels and accessible strings', async () => {
    const user = userEvent.setup();
    renderMultiSelect([], {
      label: 'التصنيفات',
      searchAriaLabel: 'بحث في التصنيفات',
      chipsAriaLabel: 'التصنيفات المختارة',
      formatSelected: (count) => (count === 0 ? 'لم يتم اختيار أي تصنيف' : `${count} تصنيفات مختارة`),
      selected: [1],
    });

    expect(screen.getByRole('button', { name: /التصنيفات 1 تصنيفات مختارة/ })).toBeInTheDocument();
    expect(screen.getByLabelText('التصنيفات المختارة')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /1 تصنيفات مختارة/ }));

    expect(screen.getByLabelText('بحث في التصنيفات')).toBeInTheDocument();
  });

  it('associates the visible label and selected count with the trigger', () => {
    renderMultiSelect([1, 2], { required: true });

    const trigger = screen.getByRole('button', { name: /الإدارات \* إدارتان مختارتان/ });
    const labelledBy = trigger.getAttribute('aria-labelledby');
    expect(labelledBy).toBeTruthy();

    const referencedElements = labelledBy?.split(' ').map((id) => document.getElementById(id));
    expect(referencedElements).toHaveLength(2);
    expect(referencedElements?.[0]).toHaveTextContent('الإدارات *');
    expect(referencedElements?.[1]).toHaveTextContent('إدارتان مختارتان');
  });

  it('resets search query after the dropdown closes', async () => {
    const user = userEvent.setup();
    renderMultiSelect();

    await user.click(screen.getByRole('button', { name: /لم يتم اختيار أي إدارة/ }));
    await user.type(screen.getByLabelText('بحث في الإدارات'), '11');
    expect(screen.getByLabelText('بحث في الإدارات')).toHaveValue('11');

    await user.keyboard('{Escape}');
    expect(screen.queryByLabelText('بحث في الإدارات')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /لم يتم اختيار أي إدارة/ }));
    expect(screen.getByLabelText('بحث في الإدارات')).toHaveValue('');
  });

  it('prevents Enter in the search input from submitting the parent form', async () => {
    const user = userEvent.setup();
    const handleSubmit = vi.fn((event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
    });

    render(
      <form onSubmit={handleSubmit}>
        <MultiSelect
          options={options}
          selected={[]}
          onChange={vi.fn()}
          label="الإدارات"
        />
      </form>,
    );

    await user.click(screen.getByRole('button', { name: /لم يتم اختيار أي إدارة/ }));
    await user.type(screen.getByLabelText('بحث في الإدارات'), '{Enter}');

    expect(handleSubmit).not.toHaveBeenCalled();
  });
});
