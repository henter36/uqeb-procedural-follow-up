import { useEffect, useRef, useState } from 'react';
import {
  formatHijriInputParts,
  gregorianToHijriParts,
  hijriToGregorianDateString,
  normalizeHijriDigits,
  parseHijriInput,
} from '../utils/hijriDateInput';
import { FUTURE_EVENT_DATE_MESSAGE, isFutureLocalDate } from '../utils/localDate';

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
  disallowFutureDate?: boolean;
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
  disallowFutureDate = false,
}: HijriDateInputProps) {
  const [text, setText] = useState(() => displayValueFromGregorian(value));
  const textRef = useRef(text);
  const [localError, setLocalError] = useState('');
  const helpId = `${id}-gregorian-help`;
  const calendarId = `${id}-calendar`;
  const describedByValue = [describedBy, value ? helpId : ''].filter(Boolean).join(' ') || undefined;
  const convertedDate = value || '';

  const formatManualInput = (rawValue: string) => {
    const digits = normalizeHijriDigits(rawValue).replace(/\D/g, '').slice(0, 8);
    const parts = [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4, 8)].filter(Boolean);
    return parts.join('/');
  };

  const updateText = (nextText: string) => {
    textRef.current = nextText;
    setText(nextText);
  };

  useEffect(() => {
    const currentParts = parseHijriInput(textRef.current);
    const currentGregorian = currentParts ? hijriToGregorianDateString(currentParts) : '';

    if (currentGregorian !== value) {
      const nextText = displayValueFromGregorian(value);
      textRef.current = nextText;
      setText(nextText);
      setLocalError('');
    }
  }, [value]);

  const applyTextValue = (nextText: string) => {
    const formattedText = formatManualInput(nextText);

    updateText(formattedText);
    if (!formattedText.trim()) {
      setLocalError('');
      onChange('');
      return;
    }

    const parts = parseHijriInput(formattedText);
    if (!parts) {
      setLocalError('التاريخ الهجري غير صالح.');
      onChange('');
      return;
    }

    const gregorian = hijriToGregorianDateString(parts);
    if (!gregorian) {
      setLocalError('التاريخ الهجري غير صالح.');
      onChange('');
      return;
    }

    if (disallowFutureDate && isFutureLocalDate(gregorian)) {
      setLocalError(FUTURE_EVENT_DATE_MESSAGE);
      onChange(gregorian);
      return;
    }

    setLocalError('');
    onChange(gregorian);
  };

  const handleCalendarChange = (nextGregorian: string) => {
    if (!nextGregorian) {
      updateText('');
      setLocalError('');
      onChange('');
      return;
    }

    updateText(displayValueFromGregorian(nextGregorian));
    if (disallowFutureDate && isFutureLocalDate(nextGregorian)) {
      setLocalError(FUTURE_EVENT_DATE_MESSAGE);
      onChange(nextGregorian);
      return;
    }

    setLocalError('');
    onChange(nextGregorian);
  };

  const handleBlur = () => {
    const parts = parseHijriInput(text);
    if (parts) {
      updateText(formatHijriInputParts(parts));
    }
  };

  return (
    <>
      <label htmlFor={id}>{label}{required ? ' *' : ''}</label>
      <input
        id={id}
        type="text"
        className="hijri-date-text-input"
        dir="ltr"
        inputMode="numeric"
        placeholder="يوم/شهر/سنة"
        value={text}
        required={required}
        disabled={disabled}
        aria-invalid={invalid || localError ? true : undefined}
        aria-describedby={describedByValue}
        data-field-name={dataFieldName}
        onChange={(event) => applyTextValue(event.target.value)}
        onBlur={handleBlur}
      />
      <input
        id={calendarId}
        type="date"
        className="hijri-date-calendar-input"
        aria-label={`${label} - اختيار من التقويم`}
        value={convertedDate}
        disabled={disabled}
        onChange={(event) => handleCalendarChange(event.target.value)}
      />
      {convertedDate && (
        <small id={helpId} className="text-muted">الموافق: {convertedDate}</small>
      )}
      {localError && <span className="field-error">{localError}</span>}
    </>
  );
}
