import { describe, expect, it } from 'vitest';
import axios from 'axios';
import { getApiErrorDetails } from './apiHelpers';

describe('getApiErrorDetails', () => {
  it('extracts message and correlation id from API error payload', () => {
    const error = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 500,
        statusText: 'Internal Server Error',
        headers: {},
        config: { headers: new axios.AxiosHeaders() },
        data: {
          errorCode: 'institutional_report_preview_failed',
          message: 'تعذر إنشاء معاينة التقرير.',
          correlationId: 'abc123',
        },
      },
    );

    const details = getApiErrorDetails(error);

    expect(details.message).toBe('تعذر إنشاء معاينة التقرير.');
    expect(details.errorCode).toBe('institutional_report_preview_failed');
    expect(details.correlationId).toBe('abc123');
    expect(details.httpStatus).toBe(500);
  });

  it('reads correlation id from response header when body omits it', () => {
    const error = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 500,
        statusText: 'Internal Server Error',
        headers: { 'x-correlation-id': 'header-correlation' },
        config: { headers: new axios.AxiosHeaders() },
        data: { message: 'فشل' },
      },
    );

    expect(getApiErrorDetails(error).correlationId).toBe('header-correlation');
  });

  it('flattens validation errors without object stringification', () => {
    const error = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      {
        status: 400,
        statusText: 'Bad Request',
        headers: {},
        config: { headers: new axios.AxiosHeaders() },
        data: {
          title: 'خطأ في التحقق',
          errors: {
            sectionIds: ['يجب تحديد قسم واحد على الأقل في التقرير.'],
          },
        },
      },
    );

    const details = getApiErrorDetails(error);

    expect(details.message).toBe('يجب تحديد قسم واحد على الأقل في التقرير.');
    expect(details.validationErrors.sectionIds).toBe('يجب تحديد قسم واحد على الأقل في التقرير.');
  });
});
