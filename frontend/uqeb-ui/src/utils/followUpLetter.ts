import { formatGregorian } from './dateUtils';

export function resolveFollowUpLetterRecipient(
  outgoingDepartments: { departmentName: string }[],
  assignments: { departmentName: string }[],
  incomingFrom?: string,
): string {
  if (outgoingDepartments.length > 0) return outgoingDepartments[0].departmentName;
  if (assignments.length > 0) return assignments[0].departmentName;
  return incomingFrom?.trim() ?? '';
}

export function buildFollowUpReferenceLine(incomingNumber?: string, incomingDate?: string): string {
  const num = incomingNumber?.trim();
  const date = incomingDate ? formatGregorian(incomingDate) : '';
  if (num && date) return `إشارةً إلى المعاملة رقم ${num} وتاريخ ${date} بشأن:`;
  if (num) return `إشارةً إلى المعاملة رقم ${num} بشأن:`;
  if (date) return `إشارةً إلى المعاملة بتاريخ ${date} بشأن:`;
  return 'إشارةً إلى المعاملة بشأن:';
}

export function buildFollowUpLetter(params: {
  subject: string;
  incomingNumber?: string;
  incomingDate?: string;
  recipient: string;
}): string {
  const reference = buildFollowUpReferenceLine(params.incomingNumber, params.incomingDate);
  const subject = params.subject.trim() || '…………';
  const recipient = params.recipient.trim() || '…………';

  return `السلام عليكم ورحمة الله وبركاته،،

${reference} ${subject}

والمحالة إلى: ${recipient}

نأمل سرعة الإفادة عما تم حيال الموضوع، وتزويدنا بما لديكم من مرئيات أو إجراءات أو مستندات ذات علاقة، وذلك خلال المدة النظامية، مع التأكيد على أهمية استكمال اللازم والرفع بما يتم حيالها.

والسلام عليكم ورحمة الله وبركاته،،`;
}
