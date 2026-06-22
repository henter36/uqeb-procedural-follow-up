import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { IconChevron } from './icons';

describe('IconChevron', () => {
  it('renders a right-pointing path for direction="right"', () => {
    const { container } = render(<IconChevron direction="right" data-testid="chevron" />);
    const path = container.querySelector('path');
    expect(path?.getAttribute('d')).toBe('M9 18l6-6-6-6');
  });

  it('renders a left-pointing path for direction="left"', () => {
    const { container } = render(<IconChevron direction="left" data-testid="chevron" />);
    const path = container.querySelector('path');
    expect(path?.getAttribute('d')).toBe('M15 18l-6-6 6-6');
  });
});
