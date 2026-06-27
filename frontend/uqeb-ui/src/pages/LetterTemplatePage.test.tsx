import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import LetterTemplatePage from './LetterTemplatePage';
import * as services from '../api/services';

vi.mock('../api/services', () => ({
  letterTemplatesApi: {
    list: vi.fn(),
    getVariables: vi.fn(),
    preview: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    copy: vi.fn(),
    setDefault: vi.fn(),
    delete: vi.fn(),
  },
}));

const template = {
  id: 1,
  code: 'follow-up-default',
  name: 'قالب افتراضي',
  content: 'مرحباً {TargetEntity}',
  templateType: 'FollowUp',
  isActive: true,
  isDefault: true,
  sortOrder: 1,
  createdAt: '2025-01-01T00:00:00Z',
  defaultSignatoryPosition: 'مدير الإدارة',
  defaultSignatoryName: 'أحمد محمد',
  defaultSignatoryRank: 'عميد',
};

describe('LetterTemplatePage', () => {
  afterEach(() => { cleanup(); });

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.letterTemplatesApi.list).mockResolvedValue({ data: [template] } as never);
    vi.mocked(services.letterTemplatesApi.getVariables).mockResolvedValue({
      data: [{ name: 'TargetEntity', arabicDescription: 'الجهة', example: 'إدارة', mayBeEmpty: false }],
    } as never);
    vi.mocked(services.letterTemplatesApi.preview).mockResolvedValue({
      data: { html: '<p>معاينة القالب</p>' },
    } as never);
  });

  it('loads templates and shows unsaved warning after edit', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <LetterTemplatePage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByDisplayValue('قالب افتراضي')).toBeInTheDocument());
    await user.type(screen.getByLabelText('نص القالب'), '!');
    expect(await screen.findByText('لديك تغييرات غير محفوظة.')).toBeInTheDocument();
  });

  it('shows default signatory fields with template values', async () => {
    render(
      <MemoryRouter>
        <LetterTemplatePage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByDisplayValue('قالب افتراضي')).toBeInTheDocument());
    await waitFor(() => expect(screen.getByLabelText('المنصب الوظيفي الافتراضي')).toHaveValue('مدير الإدارة'));
    expect(screen.getByLabelText('اسم الموقّع الافتراضي')).toHaveValue('أحمد محمد');
    expect(screen.getByLabelText('الرتبة الافتراضية')).toHaveValue('عميد');
  });

  it('includes signatory defaults in save payload', async () => {
    const user = userEvent.setup();
    vi.mocked(services.letterTemplatesApi.update).mockResolvedValue({
      data: { ...template, defaultSignatoryPosition: 'مدير جديد' },
    } as never);

    render(
      <MemoryRouter>
        <LetterTemplatePage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByLabelText('المنصب الوظيفي الافتراضي')).toHaveValue('مدير الإدارة'));

    const positionInput = screen.getByLabelText('المنصب الوظيفي الافتراضي');
    await user.clear(positionInput);
    await user.type(positionInput, 'مدير جديد');

    const saveBtn = screen.getByRole('button', { name: 'حفظ' });
    await user.click(saveBtn);

    await waitFor(() => {
      expect(services.letterTemplatesApi.update).toHaveBeenCalledWith(
        1,
        expect.objectContaining({
          defaultSignatoryPosition: 'مدير جديد',
        }),
      );
    });
  });

  it('shows variables sidebar', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <LetterTemplatePage />
      </MemoryRouter>,
    );

    // Wait for the variables summary to appear (collapsed by default)
    const summary = await screen.findByText('المتغيرات المتاحة', { exact: false });
    expect(summary).toBeInTheDocument();

    // Open the details to reveal the variables list
    await user.click(summary);
    await waitFor(() => {
      // getAllByText handles the case where the variable name also appears in the editor textarea
      expect(screen.getAllByText('{TargetEntity}').length).toBeGreaterThan(0);
    });
  });
});
