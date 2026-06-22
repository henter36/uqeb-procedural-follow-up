import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
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
  it('selects highlighted option with keyboard from input', async () => {
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

    const input = screen.getByLabelText('اختيار القائمة');
    await user.click(input);
    await user.keyboard('{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith(2);
  });

  it('selects option with mouse from native select', async () => {
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

    await user.click(screen.getByLabelText('اختيار القائمة'));
    await user.selectOptions(screen.getByLabelText('اختيار القائمة', { selector: 'select' }), '3');

    expect(onChange).toHaveBeenCalledWith(3);
  });

  it('closes list on Escape', async () => {
    const user = userEvent.setup();

    render(
      <SearchableSelect
        label="اختيار القائمة"
        value=""
        onChange={vi.fn()}
        options={options}
      />,
    );

    await user.click(screen.getByLabelText('اختيار القائمة'));
    expect(screen.getByRole('combobox', { expanded: true })).toBeInTheDocument();

    await user.keyboard('{Escape}');
    expect(screen.getByRole('combobox', { expanded: false })).toBeInTheDocument();
    expect(document.querySelector('select.searchable-select-list')).toBeNull();
  });

  it('renders inactive and sublabel text in native options', async () => {
    const user = userEvent.setup();

    render(
      <SearchableSelect
        label="اختيار القائمة"
        value=""
        onChange={vi.fn()}
        options={options}
      />,
    );

    await user.click(screen.getByLabelText('اختيار القائمة'));

    expect(screen.getByRole('option', { name: 'Gamma (غير نشط) — G-01' })).toBeInTheDocument();
    expect(document.querySelector('div[role="listbox"]')).toBeNull();
    expect(document.querySelector('button[role="option"]')).toBeNull();
  });
});
