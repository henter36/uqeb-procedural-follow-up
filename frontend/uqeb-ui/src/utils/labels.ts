export const statusLabels: Record<string, string> = {
  New: 'جديدة',
  InProgress: 'قيد الإجراء',
  Assigned: 'محالة',
  WaitingForReply: 'بانتظار رد',
  PartiallyReplied: 'رد جزئي',
  ReadyForResponse: 'جاهزة للإفادة',
  ResponseCompleted: 'تمت الإفادة',
  Closed: 'مغلقة',
  Overdue: 'متأخرة',
  Cancelled: 'ملغاة',
  Archived: 'مؤرشفة',
};

export const responseTypeLabels: Record<string, string> = {
  External: 'إفادة للجهة',
  Internal: 'إفادة داخلية',
  Both: 'إفادة للجهة وداخلية',
  None: '—',
};

export const priorityLabels: Record<string, string> = {
  Normal: 'عادي',
  Urgent: 'عاجل',
  VeryUrgent: 'عاجل جداً',
};

export const replyStatusLabels: Record<string, string> = {
  Pending: 'بانتظار الرد',
  Replied: 'تم الرد',
  Overdue: 'متأخر',
};

export const auditActionLabels: Record<string, string> = {
  Create: 'إنشاء',
  Update: 'تحديث',
  StatusChange: 'تغيير الحالة',
  AddFollowUp: 'إضافة تعقيب',
  AddAssignment: 'إضافة تحويل',
  RecordReply: 'تسجيل رد',
  RecordResponse: 'تسجيل إفادة',
  CompleteResponse: 'تسجيل الإفادة',
  Close: 'إغلاق',
  Cancel: 'إلغاء',
  Archive: 'أرشفة',
  CloseAttemptFailed: 'محاولة إغلاق فاشلة',
};

export const roleLabels: Record<string, string> = {
  Admin: 'مدير',
  Supervisor: 'مشرف',
  DataEntry: 'مدخل بيانات',
  DepartmentUser: 'موظف إدارة',
  Reader: 'قارئ',
};

export function statusBadgeClass(status: string, isOverdue?: boolean): string {
  if (isOverdue) return 'badge-red';
  switch (status) {
    case 'Closed': case 'Archived': return 'badge-gray';
    case 'ResponseCompleted': return 'badge-green';
    case 'WaitingForReply': case 'PartiallyReplied': return 'badge-orange';
    case 'Overdue': return 'badge-red';
    case 'Cancelled': return 'badge-dark';
    default: return 'badge-blue';
  }
}
