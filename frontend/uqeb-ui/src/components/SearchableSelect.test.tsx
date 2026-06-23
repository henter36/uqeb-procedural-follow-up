import { cleanup, render, screen, within } from '@testing-library/react';
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

function getResultsSelect() {
  return screen.getByRole('listbox', { name: 'اختيار القائمة - النتائج' });
}

describe('SearchableSelect accessibility', () => {
  it('renders combobox and native select results when open', async () => {
    const user = userEvent.setup();
    renderSelect();

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    expect(input).toHaveAttribute('role', 'combobox');
    expect(input).not.toHaveAttribute('aria-activedescendant');

    await user.click(input);

    const select = getResultsSelect();
    expect(select).toHaveClass('searchable-select-list');
    expect(select).toHaveAttribute('tabindex', '-1');
    expect(within(select).getAllByRole('option')).toHaveLength(3);
    expect(document.querySelector('ul[role="listbox"]')).toBeNull();
    expect(document.querySelector('[role="option"][tabindex]')).toBeNull();
  });

  it('keeps native select out of the tab order', async () => {
    const user = userEvent.setup();
    render(
      <div>
        <SearchableSelect label="اختيار القائمة" value="" onChange={vi.fn()} options={options} />
        <button type="button">التالي</button>
      </div>,
    );

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    expect(getResultsSelect()).toHaveAttribute('tabindex', '-1');

    await user.tab();
    expect(screen.getByRole('button', { name: 'التالي' })).toHaveFocus();
  });

  it('syncs native select highlight when navigating with ArrowDown', async () => {
    const user = userEvent.setup();
    renderSelect();

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);

    const select = getResultsSelect();
    expect(select).toHaveValue('1');

    await user.keyboard('{ArrowDown}');
    expect(select).toHaveValue('2');
    expect(document.activeElement).toBe(input);
  });

  it('selects active option with Enter from input', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    renderSelect(onChange);

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    await user.keyboard('{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith(2);
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('closes results on Escape', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    renderSelect(onChange, 1);

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);
    expect(getResultsSelect()).toBeInTheDocument();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('listbox', { name: 'اختيار القائمة - النتائج' })).not.toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
    expect(input).toHaveValue('Alpha');
  });

  it('does not move DOM focus to native options', async () => {
    const user = userEvent.setup();
    renderSelect();

    const input = screen.getByRole('combobox', { name: 'اختيار القائمة' });
    await user.click(input);
    await user.keyboard('{ArrowDown}{ArrowDown}');

    expect(document.activeElement).toBe(input);
  });
});

describe('SearchableSelect interaction', () => {
  it('selects option on click and fires onChange once', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    renderSelect(onChange);

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    await user.click(screen.getByRole('option', { name: 'Beta' }));

    expect(onChange).toHaveBeenCalledWith(2);
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('listbox', { name: 'اختيار القائمة - النتائج' })).not.toBeInTheDocument();
  });

  it('selects option with user.selectOptions', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    renderSelect(onChange);

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    await user.selectOptions(getResultsSelect(), '2');

    expect(onChange).toHaveBeenCalledWith(2);
    expect(onChange).toHaveBeenCalledTimes(1);
  });

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
    expect(screen.queryByRole('listbox', { name: 'اختيار القائمة - النتائج' })).not.toBeInTheDocument();
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

  it('does not use custom listbox markup', async () => {
    const user = userEvent.setup();
    renderSelect();

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));

    expect(document.querySelector('ul[role="listbox"]')).toBeNull();
    expect(document.querySelector('select.searchable-select-list')).toBeTruthy();
    expect(document.querySelector('[role="presentation"]')).toBeNull();
  });

  it('supports arrow navigation before Enter selection', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    renderSelect(onChange);

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith(3);
  });

  it('renders inactive and sublabel text in native options', async () => {
    const user = userEvent.setup();
    renderSelect();

    await user.click(screen.getByRole('combobox', { name: 'اختيار القائمة' }));
    expect(screen.getByRole('option', { name: 'Gamma (غير نشط) — G-01' })).toBeInTheDocument();
  });
});
