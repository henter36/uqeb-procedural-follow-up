export type WorkspaceAction =
  | 'assignment'
  | 'followup'
  | 'attachment'
  | 'reply-assignment'
  | 'reply-followup'
  | 'complete-response'
  | 'follow-up-letter'
  | 'admin-edit-assignment'
  | 'admin-edit-dates';

export type WorkspaceActionContext = Readonly<{
  replyAssignmentId?: number;
  replyFollowUpId?: number;
  adminEditAssignmentId?: number;
}>;
