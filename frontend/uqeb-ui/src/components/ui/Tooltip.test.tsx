import { describe, expect, it, afterEach } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import Tooltip from './Tooltip';

describe('Tooltip', () => {
  afterEach(() => {
    cleanup();
  });

  it('renders the content in an accessible, keyboard-focusable tooltip linked via aria-describedby', () => {
    render(
      <Tooltip content="شرح إضافي للمؤشر">
        <span>ⓘ</span>
      </Tooltip>,
    );

    const tooltip = screen.getByRole('tooltip', { hidden: true });
    expect(tooltip).toHaveTextContent('شرح إضافي للمؤشر');

    const wrapper = tooltip.parentElement;
    expect(wrapper).toHaveAttribute('tabindex', '0');
    expect(wrapper).toHaveAttribute('aria-describedby', tooltip.getAttribute('id'));
  });
});
