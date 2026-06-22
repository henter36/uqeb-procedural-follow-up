import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import SearchableSelect from './SearchableSelect';

const options = [
  { id: 1, name: 'Alpha' },
  { id: 2, name: 'Beta' },
];

afterEach(() => {
  cleanup();
});

describe('SearchableSelect', () => {
  it('selects highlighted option with keyboard', async () => {
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
});
