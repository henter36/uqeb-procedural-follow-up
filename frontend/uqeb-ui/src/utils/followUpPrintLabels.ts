import type { FollowUpPrintJobPartStatus, FollowUpPrintJobStatus, LetterTemplateType } from '../api/types';

export const followUpPrintJobStatusLabels: Record<FollowUpPrintJobStatus, string> = {
  Queued: 'في الانتظار',
  Claimed: 'تمت المطالبة',
  Processing: 'قيد المعالجة',
  ReadyToPrint: 'جاهز للطباعة',
  PartiallyPrinted: 'طُبع جزئياً',
  Completed: 'مكتمل',
  Failed: 'فشل',
  Cancelled: 'ملغى',
  Expired: 'منتهي',
};

export const followUpPrintJobPartStatusLabels: Record<FollowUpPrintJobPartStatus, string> = {
  Pending: 'في الانتظار',
  Processing: 'قيد المعالجة',
  ReadyToPrint: 'جاهز للطباعة',
  Printed: 'طُبع',
  Failed: 'فشل',
  Cancelled: 'ملغى',
};

export const letterTemplateTypeLabels: Record<LetterTemplateType, string> = {
  FollowUp: 'تعقيب عام',
  FirstFollowUp: 'التعقيب الأول',
  SecondFollowUp: 'التعقيب الثاني',
  UrgentFollowUp: 'تعقيب عاجل',
  FinalFollowUp: 'التعقيب الأخير',
  LateReply: 'تأخر الرد',
  CompletionRequest: 'طلب إنجاز',
  InternalFollowUp: 'تعقيب داخلي',
  ExternalFollowUp: 'تعقيب خارجي',
};

export function followUpPrintJobStatusBadgeClass(status: FollowUpPrintJobStatus): string {
  switch (status) {
    case 'Completed': return 'badge-green';
    case 'ReadyToPrint':
    case 'PartiallyPrinted': return 'badge-blue';
    case 'Failed': return 'badge-red';
    case 'Cancelled':
    case 'Expired': return 'badge-gray';
    case 'Processing':
    case 'Claimed': return 'badge-orange';
    default: return 'badge-yellow';
  }
}

export function followUpPrintPartStatusBadgeClass(status: FollowUpPrintJobPartStatus): string {
  switch (status) {
    case 'Printed': return 'badge-green';
    case 'ReadyToPrint': return 'badge-blue';
    case 'Failed': return 'badge-red';
    case 'Cancelled': return 'badge-gray';
    case 'Processing': return 'badge-orange';
    default: return 'badge-yellow';
  }
}
