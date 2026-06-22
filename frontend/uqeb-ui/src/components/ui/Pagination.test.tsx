import { describe, expect, it, vi, afterEach } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import Pagination from './Pagination';

describe('Pagination', () => {
  afterEach(() => {
    cleanup();
  });

  it('clamps page display when page exceeds totalPages', () => {
    render(
      <Pagination
        page={5}
        pageSize={20}
        total={30}
        itemCount={10}
        onPageChange={vi.fn()}
      />,
    );

    expect(screen.getByText(/عرض/)).toHaveTextContent('عرض 21–30 من 30');
    expect(screen.getByText(/صفحة/)).toHaveTextContent('صفحة 2 من 2');
  });

  it('shows zero range when total is zero', () => {
    render(
      <Pagination
        page={3}
        pageSize={20}
        total={0}
        itemCount={0}
        onPageChange={vi.fn()}
      />,
    );

    expect(screen.getByText(/عرض/)).toHaveTextContent('عرض 0–0 من 0');
    expect(screen.getByText(/صفحة/)).toHaveTextContent('صفحة 1 من 1');
  });

  it('uses nav element with aria-label', () => {
    const { container } = render(
      <Pagination page={1} pageSize={10} total={5} itemCount={5} onPageChange={vi.fn()} />,
    );
    const nav = container.querySelector('nav.pagination');
    expect(nav).toHaveAttribute('aria-label', 'التصفح');
  });

  it('navigates using clamped page values', async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    const { container } = render(
      <Pagination
        page={5}
        pageSize={20}
        total={30}
        itemCount={10}
        onPageChange={onPageChange}
      />,
    );

    const prevButton = container.querySelector('button[aria-label="الصفحة السابقة"]');
    expect(prevButton).toBeTruthy();
    await user.click(prevButton!);
    expect(onPageChange).toHaveBeenCalledWith(1);
  });
});
