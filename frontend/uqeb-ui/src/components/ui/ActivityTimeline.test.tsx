import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import ActivityTimeline from './ActivityTimeline';

describe('ActivityTimeline', () => {
  it('renders events as an ordered list', () => {
    const { container } = render(
      <ActivityTimeline
        events={[
          { id: 1, action: 'إنشاء', date: '2026-01-01', userName: 'أحمد' },
          { id: 2, action: 'تحديث', date: '2026-01-02' },
        ]}
      />,
    );

    const list = container.querySelector('ol.activity-timeline');
    expect(list).toBeTruthy();
    expect(list?.querySelectorAll('li')).toHaveLength(2);
    expect(screen.getByText('إنشاء')).toBeInTheDocument();
  });

  it('shows empty label when there are no events', () => {
    render(<ActivityTimeline events={[]} emptyLabel="لا أحداث" />);
    expect(screen.getByText('لا أحداث')).toBeInTheDocument();
  });
});
