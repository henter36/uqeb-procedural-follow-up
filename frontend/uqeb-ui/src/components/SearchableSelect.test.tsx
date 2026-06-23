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

describe('SearchableSelect', () => {
  it('selects highlighted option with keyboard Enter from input', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(
      <SearchableSelect
        label="اختيار القائمة"
        value=""
        onChange={onChange}
        options={options}
      />,
    );

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);
    await user.keyboard('{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith(2);
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('selects option immediately on mouse click', async () => {
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
    expect(screen.getByRole('combobox', { name: 'اختيار القائمة' })).toHaveValue('Gamma (غير نشط)');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('closes list on Escape without changing value', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(
      <SearchableSelect
        label="اختيار القائمة"
        value={1}
        onChange={onChange}
        options={options}
      />,
    );

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);
    expect(screen.getByRole('listbox')).toBeInTheDocument();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
    expect(input).toHaveValue('Alpha');
  });

  it('does not clear selected value on blur', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(
      <div>
        <SearchableSelect
          label="اختيار القائمة"
          value={2}
          onChange={onChange}
          options={options}
        />
        <button type="button">خارج</button>
      </div>,
    );

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);
    await user.click(screen.getByRole('button', { name: 'خارج' }));

    expect(onChange).not.toHaveBeenCalled();
    expect(input).toHaveValue('Beta');
  });

  it('renders inactive and sublabel text in listbox options', async () => {
    const user = userEvent.setup();

    render(
      <SearchableSelect
        label="اختيار القائمة"
        value=""
        onChange={vi.fn()}
        options={options}
      />,
    );

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));

    expect(screen.getByRole('option', { name: 'Gamma (غير نشط) — G-01' })).toBeInTheDocument();
    expect(document.querySelector('select.searchable-select-list')).toBeNull();
  });

  it('supports arrow key navigation before Enter selection', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(
      <SearchableSelect
        label="اختيار القائمة"
        value=""
        onChange={onChange}
        options={options}
      />,
    );

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith(3);
  });
});
