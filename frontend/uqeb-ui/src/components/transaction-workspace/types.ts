export type WorkspaceAction =
  | 'assignment'
  | 'followup'
  | 'attachment'
  | 'reply-assignment'
  | 'admin-edit-assignment-reply'
  | 'reply-followup'
  | 'admin-edit-followup-reply'
  | 'complete-response'
  | 'follow-up-letter'
  | 'admin-edit-assignment'
  | 'admin-edit-dates'
  | 'admin-edit-transaction-response'
  | 'enable-recurring';

export type WorkspaceActionContext = Readonly<{
  replyAssignmentId?: number;
  replyFollowUpId?: number;
  adminEditAssignmentId?: number;
}>;
