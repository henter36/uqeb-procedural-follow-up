import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import LetterTemplatePage from './LetterTemplatePage';
import * as services from '../api/services';

vi.mock('../api/services', () => ({
  letterTemplatesApi: {
    list: vi.fn(),
    getVariables: vi.fn(),
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
};

describe('LetterTemplatePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.letterTemplatesApi.list).mockResolvedValue({ data: [template] } as never);
    vi.mocked(services.letterTemplatesApi.getVariables).mockResolvedValue({
      data: [{ name: 'TargetEntity', arabicDescription: 'الجهة', example: 'إدارة', mayBeEmpty: false }],
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

  it('shows variables sidebar', async () => {
    render(
      <MemoryRouter>
        <LetterTemplatePage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('المتغيرات')).toBeInTheDocument();
      expect(screen.getByText('{TargetEntity}')).toBeInTheDocument();
    });
  });
});
