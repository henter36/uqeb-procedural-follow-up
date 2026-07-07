import { describe, expect, it, afterEach } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import Tooltip from './Tooltip';

describe('Tooltip', () => {
  afterEach(() => {
    cleanup();
  });

  it('renders the content in an accessible tooltip linked to an interactive button trigger via aria-describedby', () => {
    render(<Tooltip content="شرح إضافي للمؤشر" />);

    const tooltip = screen.getByRole('tooltip', { hidden: true });
    expect(tooltip).toHaveTextContent('شرح إضافي للمؤشر');

    const trigger = screen.getByRole('button', { name: 'عرض التوضيح' });
    expect(trigger).toHaveAttribute('aria-describedby', tooltip.getAttribute('id'));
  });

  it('uses a custom triggerLabel as the button accessible name when provided', () => {
    render(<Tooltip content="شرح إضافي للمؤشر" triggerLabel="شرح مؤشر تاريخ الوارد" />);

    const trigger = screen.getByRole('button', { name: 'شرح مؤشر تاريخ الوارد' });
    const tooltip = screen.getByRole('tooltip', { hidden: true });
    expect(trigger).toHaveAttribute('aria-describedby', tooltip.getAttribute('id'));
  });
});
