import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { PagedResult } from '../api/types';
import { ReferenceDataPage } from '../components/ReferenceDataPage';

type Item = { id: number; name: string };

const columns = [{ key: 'name', label: 'الاسم', render: (item: Item) => item.name }];

const pageWithItem: PagedResult<Item> = {
  items: [{ id: 1, name: 'أ' }],
  totalCount: 1,
  totalPages: 1,
  page: 1,
  pageSize: 20,
  hasNextPage: false,
  hasPreviousPage: false,
};

function createFetchMock() {
  let calls = 0;
  return vi.fn(async () => {
    calls += 1;
    if (calls === 1) throw new Error('fail');
    return { data: pageWithItem };
  });
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('ReferenceDataPage', () => {
  it('clears page error when opening create', async () => {
    const fetchPage = createFetchMock();

    render(
      <ReferenceDataPage<Item>
        title="اختبار"
        addLabel="إضافة"
        columns={columns}
        fetchPage={fetchPage}
        getRowId={(item) => item.id}
        renderForm={({ onClose }) => <button type="button" onClick={onClose}>إغلاق</button>}
      />,
    );

    await waitFor(() => expect(screen.getByText('تعذر تحميل البيانات')).toBeInTheDocument());
    fireEvent.click(screen.getByText('إضافة'));
    expect(screen.queryByText('تعذر تحميل البيانات')).not.toBeInTheDocument();
  });

  it('clears page error when opening edit', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
    const fetchPage = vi.fn().mockResolvedValue({ data: pageWithItem });
    const onDeactivate = vi.fn().mockRejectedValue(new Error('failed'));

    render(
      <ReferenceDataPage<Item>
        title="اختبار"
        addLabel="إضافة"
        columns={columns}
        fetchPage={fetchPage}
        getRowId={(item) => item.id}
        onDeactivate={onDeactivate}
        renderForm={({ onClose }) => <button type="button" onClick={onClose}>إغلاق</button>}
      />,
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'تعطيل' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'تعطيل' }));
    await waitFor(() => expect(screen.getByText('تعذر تحديث الحالة')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'تعديل' }));
    expect(screen.queryByText('تعذر تحديث الحالة')).not.toBeInTheDocument();
  });

  it('clears error after successful save', async () => {
    const fetchPage = vi.fn(async () => ({ data: pageWithItem }));

    render(
      <ReferenceDataPage<Item>
        title="اختبار"
        addLabel="إضافة"
        columns={columns}
        fetchPage={fetchPage}
        getRowId={(item) => item.id}
        renderForm={({ onSaved }) => (
          <button type="button" onClick={() => onSaved({ id: 2, name: 'جديد' })}>حفظ</button>
        )}
      />,
    );

    await waitFor(() => expect(screen.getByText('إضافة')).toBeInTheDocument());
    fireEvent.click(screen.getByText('إضافة'));
    fireEvent.click(screen.getByText('حفظ'));
    await waitFor(() => expect(screen.getByText('تم الحفظ بنجاح')).toBeInTheDocument());
    expect(screen.queryByText('تعذر تحميل البيانات')).not.toBeInTheDocument();
  });

  it('shows only success after deactivate succeeds', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
    const fetchPage = vi.fn().mockResolvedValue({ data: pageWithItem });
    const onDeactivate = vi.fn().mockResolvedValue(undefined);

    render(
      <ReferenceDataPage<Item>
        title="اختبار"
        addLabel="إضافة"
        columns={columns}
        fetchPage={fetchPage}
        getRowId={(item) => item.id}
        onDeactivate={onDeactivate}
        renderForm={() => null}
      />,
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'تعطيل' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'تعطيل' }));

    await waitFor(() => expect(screen.getByText('تم تحديث الحالة بنجاح')).toBeInTheDocument());
    expect(screen.queryByText('تعذر تحديث الحالة')).not.toBeInTheDocument();
  });

  it('shows only error after deactivate fails', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
    const fetchPage = vi.fn().mockResolvedValue({ data: pageWithItem });
    const onDeactivate = vi.fn().mockRejectedValue(new Error('failed'));

    render(
      <ReferenceDataPage<Item>
        title="اختبار"
        addLabel="إضافة"
        columns={columns}
        fetchPage={fetchPage}
        getRowId={(item) => item.id}
        onDeactivate={onDeactivate}
        renderForm={() => null}
      />,
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'تعطيل' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'تعطيل' }));

    await waitFor(() => expect(screen.getByText('تعذر تحديث الحالة')).toBeInTheDocument());
    expect(screen.queryByText('تم تحديث الحالة بنجاح')).not.toBeInTheDocument();
  });
});
