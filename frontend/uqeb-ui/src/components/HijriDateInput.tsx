import { useMemo, useState } from 'react';
import {
  formatHijriInputParts,
  gregorianToHijriParts,
  hijriToGregorianDateString,
  parseHijriInput,
} from '../utils/hijriDateInput';

type HijriDateInputProps = Readonly<{
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
  disabled?: boolean;
  invalid?: boolean;
  describedBy?: string;
  dataFieldName?: string;
}>;

function displayValueFromGregorian(value: string): string {
  if (!value) return '';
  const parts = gregorianToHijriParts(value);
  return parts ? formatHijriInputParts(parts) : '';
}

export default function HijriDateInput({
  id,
  label,
  value,
  onChange,
  required = false,
  disabled = false,
  invalid = false,
  describedBy,
  dataFieldName,
}: HijriDateInputProps) {
  const [text, setText] = useState(() => displayValueFromGregorian(value));
  const [localError, setLocalError] = useState('');
  const helpId = `${id}-gregorian-help`;
  const describedByValue = [describedBy, value ? helpId : ''].filter(Boolean).join(' ') || undefined;

  const convertedDate = useMemo(() => value || '', [value]);

  const handleChange = (nextText: string) => {
    setText(nextText);
    if (!nextText.trim()) {
      setLocalError('');
      onChange('');
      return;
    }

    const parts = parseHijriInput(nextText);
    if (!parts) {
      setLocalError('التاريخ الهجري غير صالح.');
      return;
    }

    const gregorian = hijriToGregorianDateString(parts);
    if (!gregorian) {
      setLocalError('التاريخ الهجري غير صالح.');
      return;
    }

    setLocalError('');
    setText(formatHijriInputParts(parts));
    onChange(gregorian);
  };

  return (
    <>
      <label htmlFor={id}>{label}{required ? ' *' : ''}</label>
      <input
        id={id}
        type="text"
        inputMode="numeric"
        placeholder="1448/01/10"
        value={text}
        required={required}
        disabled={disabled}
        aria-invalid={invalid || localError ? true : undefined}
        aria-describedby={describedByValue}
        data-field-name={dataFieldName}
        onChange={(event) => handleChange(event.target.value)}
      />
      {convertedDate && (
        <small id={helpId} className="text-muted">الموافق: {convertedDate}</small>
      )}
      {localError && <span className="field-error">{localError}</span>}
    </>
  );
}
