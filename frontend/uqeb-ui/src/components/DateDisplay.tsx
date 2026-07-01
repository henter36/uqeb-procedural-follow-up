import { formatHijri } from '../utils/dateUtils';

type DateDisplayProps = Readonly<{
  date: string;
}>;

export default function DateDisplay({ date }: DateDisplayProps) {
  if (!date) return <span>-</span>;
  return <span>{formatHijri(date)}</span>;
}
