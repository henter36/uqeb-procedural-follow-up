import { formatDualDate } from '../utils/dateUtils';

export default function DateDisplay({ date, showHijri = true }: { date: string; showHijri?: boolean }) {
  if (!date) return <span>-</span>;
  return <span title={showHijri ? formatDualDate(date) : undefined}>
    {showHijri ? formatDualDate(date) : new Date(date).toLocaleDateString('ar-SA')}
  </span>;
}
