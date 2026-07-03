import { useEffect, useRef, useState, type ClipboardEvent } from 'react';
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

function cleanManualInput(rawValue: string): string {
  return normalizeHijriDigits(rawValue)
    .replaceAll('-', '/')
    .replace(/[^\d/]/g, '')
    .slice(0, 10);
}

function formatLinearDigits(rawValue: string): string {
  const digits = normalizeHijriDigits(rawValue).replace(/\D/g, '').slice(0, 8);
  const parts = [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4, 8)].filter(Boolean);
  return parts.join('/');
}

function hasDateSeparator(value: string): boolean {
  return value.includes('/') || value.includes('-');
}

function digitCount(value: string): number {
  return normalizeHijriDigits(value).replace(/\D/g, '').length;
}

function isDeletingInput(inputType: string): boolean {
  return inputType.startsWith('delete');
}

function normalizeCompleteHijriInput(value: string): string | null {
  const parts = parseHijriInput(value);
  return parts ? formatHijriInputParts(parts) : null;
}

function getManualInputText(rawValue: string, isLinearEdit: boolean, isPaste: boolean): string {
  const cleaned = cleanManualInput(rawValue);
  const normalized = normalizeCompleteHijriInput(cleaned);

  if (isPaste && normalized) return normalized;
  if (hasDateSeparator(rawValue)) {
    if (normalized && isLinearEdit) return normalized;
    return isLinearEdit ? formatLinearDigits(rawValue) : cleaned;
  }
  if (isLinearEdit) return formatLinearDigits(rawValue);
  return cleaned;
}

function toGregorianFromHijriText(value: string): string | null | undefined {
  if (!value.trim()) return '';

  const parts = parseHijriInput(value);
  if (!parts) return null;
  return hijriToGregorianDateString(parts);
}

function getDateInputError(gregorian: string | null, disallowFutureDate: boolean): string {
  if (!gregorian) return 'التاريخ الهجري غير صالح.';
  return disallowFutureDate && isFutureLocalDate(gregorian) ? FUTURE_EVENT_DATE_MESSAGE : '';
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

  const applyParsedValue = (gregorian: string | null | undefined) => {
    if (gregorian === '') {
      setLocalError('');
      onChange('');
      return;
    }

    const error = getDateInputError(gregorian ?? null, disallowFutureDate);
    if (error) {
      setLocalError(error);
      onChange(gregorian ?? '');
      return;
    }

    setLocalError('');
    onChange(gregorian ?? '');
  };

  const applyTextValue = (nextText: string, isLinearEdit: boolean, isPaste: boolean) => {
    const formattedText = getManualInputText(nextText, isLinearEdit, isPaste);
    updateText(formattedText);
    applyParsedValue(toGregorianFromHijriText(formattedText));
  };

  const handlePaste = (event: ClipboardEvent<HTMLInputElement>) => {
    const pastedText = event.clipboardData.getData('text');
    const normalized = normalizeCompleteHijriInput(cleanManualInput(pastedText));
    if (!normalized) return;

    event.preventDefault();
    updateText(normalized);
    applyParsedValue(toGregorianFromHijriText(normalized));
  };

  const handleCalendarChange = (nextGregorian: string) => {
    if (!nextGregorian) {
      updateText('');
      setLocalError('');
      onChange('');
      return;
    }

    updateText(displayValueFromGregorian(nextGregorian));
    applyParsedValue(nextGregorian);
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
        onChange={(event) => {
          const isAtEnd = event.target.selectionStart === event.target.value.length
            && event.target.selectionEnd === event.target.value.length;
          const inputType = 'inputType' in event.nativeEvent ? String(event.nativeEvent.inputType) : '';
          const hasMoreDigits = digitCount(event.target.value) >= digitCount(textRef.current);
          const isLinearEdit = isAtEnd || (hasMoreDigits && !isDeletingInput(inputType));
          applyTextValue(event.target.value, isLinearEdit, inputType === 'insertFromPaste');
        }}
        onPaste={handlePaste}
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
