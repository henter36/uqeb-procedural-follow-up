import { formatHijri } from '../utils/dateUtils';

export default function DateDisplay({ date }: { date: string }) {
  if (!date) return <span>-</span>;
  return <span>{formatHijri(date)}</span>;
}
