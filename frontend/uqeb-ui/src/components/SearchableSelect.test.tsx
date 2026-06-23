import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useState } from 'react';
import SearchableSelect from './SearchableSelect';

const options = [
  { id: 1, name: 'Alpha' },
  { id: 2, name: 'Beta' },
  { id: 3, name: 'Gamma', subLabel: 'G-01', isActive: false },
];

afterEach(() => {
  cleanup();
});

function renderSelect(onChange = vi.fn(), value: number | '' = '') {
  return render(
    <SearchableSelect
      label="اختيار القائمة"
      value={value}
      onChange={onChange}
      options={options}
    />,
  );
}

describe('SearchableSelect accessibility', () => {
  it('renders combobox, listbox, and option roles', async () => {
    const user = userEvent.setup();
    renderSelect();

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    expect(input).toHaveAttribute('role', 'combobox');

    await user.click(input);

    const listbox = screen.getByRole('listbox', { name: 'اختيار القائمة' });
    expect(listbox).toHaveAttribute('id');
    expect(screen.getAllByRole('option')).toHaveLength(3);
  });

  it('keeps options out of the tab order', async () => {
    const user = userEvent.setup();
    render(
      <div>
        <SearchableSelect label="اختيار القائمة" value="" onChange={vi.fn()} options={options} />
        <button type="button">التالي</button>
      </div>,
    );

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));

    const optionElements = screen.getAllByRole('option');
    optionElements.forEach((option) => {
      expect(option).toHaveAttribute('tabindex', '-1');
      expect(option).not.toHaveFocus();
    });

    await user.tab();
    expect(screen.getByRole('button', { name: 'التالي' })).toHaveFocus();
  });

  it('updates aria-activedescendant with ArrowDown', async () => {
    const user = userEvent.setup();
    renderSelect();

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);
    expect(input).toHaveAttribute('aria-activedescendant', expect.stringContaining('-option-1'));

    await user.keyboard('{ArrowDown}');
    expect(input).toHaveAttribute('aria-activedescendant', expect.stringContaining('-option-2'));
  });

  it('selects active option with Enter', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    renderSelect(onChange);

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    await user.keyboard('{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith(2);
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('closes list on Escape', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    renderSelect(onChange, 1);

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);
    expect(screen.getByRole('listbox')).toBeInTheDocument();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
    expect(input).toHaveValue('Alpha');
  });

  it('does not move DOM focus to option elements', async () => {
    const user = userEvent.setup();
    renderSelect();

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);
    await user.keyboard('{ArrowDown}{ArrowDown}');

    expect(document.activeElement).toBe(input);
    expect(screen.getAllByRole('option').some((option) => option === document.activeElement)).toBe(false);
  });
});

describe('SearchableSelect interaction', () => {
  it('selects option immediately on mouse click without Enter', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    function Harness() {
      const [value, setValue] = useState<number | ''>('');
      return (
        <SearchableSelect
          label="اختيار القائمة"
          value={value}
          onChange={(id) => {
            setValue(id);
            onChange(id);
          }}
          options={options}
        />
      );
    }

    render(<Harness />);

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    await user.click(screen.getByRole('option', { name: 'Gamma (غير نشط) — G-01' }));

    expect(onChange).toHaveBeenCalledWith(3);
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('combobox', { name: 'اختيار القائمة' })).toHaveValue('Gamma (غير نشط)');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('does not clear selected value on blur', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(
      <div>
        <SearchableSelect label="اختيار القائمة" value={2} onChange={onChange} options={options} />
        <button type="button">خارج</button>
      </div>,
    );

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);
    await user.click(screen.getByRole('button', { name: 'خارج' }));

    expect(onChange).not.toHaveBeenCalled();
    expect(input).toHaveValue('Beta');
  });

  it('returns option.id from selection', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    renderSelect(onChange);

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    await user.click(screen.getByRole('option', { name: 'Beta' }));

    expect(onChange).toHaveBeenCalledWith(2);
  });

  it('does not use role=presentation wrappers in the listbox', async () => {
    const user = userEvent.setup();
    renderSelect();

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));

    expect(document.querySelector('[role="presentation"]')).toBeNull();
    expect(document.querySelector('select.searchable-select-list')).toBeNull();
  });

  it('supports arrow navigation before Enter selection', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    renderSelect(onChange);

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith(3);
  });

  it('renders inactive and sublabel text in listbox options', async () => {
    const user = userEvent.setup();
    renderSelect();

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    expect(screen.getByRole('option', { name: 'Gamma (غير نشط) — G-01' })).toBeInTheDocument();
  });
});
