import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';

export type SelectOption = {
  id: number;
  name: string;
  isActive?: boolean;
  subLabel?: string;
};

type SearchableSelectProps = {
  label: string;
  value: number | '';
  onChange: (value: number | '') => void;
  options: SelectOption[];
  placeholder?: string;
  allowClear?: boolean;
  disabled?: boolean;
  required?: boolean;
  onSearch?: (term: string) => void;
  loading?: boolean;
  debounceMs?: number;
};

export default function SearchableSelect({
  label,
  value,
  onChange,
  options,
  placeholder = 'ابحث أو اختر...',
  allowClear = false,
  disabled = false,
  required = false,
  onSearch,
  loading = false,
  debounceMs = 300,
}: SearchableSelectProps) {
  const inputId = useId();
  const listboxId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [highlightIndex, setHighlightIndex] = useState(0);

  const selected = useMemo(
    () => options.find((o) => o.id === value) ?? null,
    [options, value],
  );

  const filtered = useMemo(() => {
    if (onSearch) return options;
    const term = query.trim().toLowerCase();
    if (!term) return options;
    return options.filter((o) =>
      o.name.toLowerCase().includes(term) ||
      (o.subLabel?.toLowerCase().includes(term) ?? false));
  }, [options, query, onSearch]);

  useEffect(() => {
    if (!onSearch) return undefined;
    const timer = window.setTimeout(() => onSearch(query.trim()), debounceMs);
    return () => window.clearTimeout(timer);
  }, [query, onSearch, debounceMs]);

  useEffect(() => {
    setHighlightIndex(0);
  }, [filtered.length, query]);

  useEffect(() => {
    const onDocClick = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDocClick);
    return () => document.removeEventListener('mousedown', onDocClick);
  }, []);

  const displayValue = open ? query : (selected
    ? `${selected.name}${selected.isActive === false ? ' (غير نشط)' : ''}`
    : '');

  const selectOption = useCallback((option: SelectOption) => {
    onChange(option.id);
    setQuery('');
    setOpen(false);
  }, [onChange]);

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!open && (e.key === 'ArrowDown' || e.key === 'Enter')) {
      setOpen(true);
      return;
    }
    if (e.key === 'Escape') {
      setOpen(false);
      setQuery('');
      return;
    }
    if (!open) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setHighlightIndex((i) => Math.min(i + 1, Math.max(filtered.length - 1, 0)));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHighlightIndex((i) => Math.max(i - 1, 0));
    } else if (e.key === 'Enter' && filtered[highlightIndex]) {
      e.preventDefault();
      selectOption(filtered[highlightIndex]);
    }
  };

  return (
    <div className={`searchable-select${disabled ? ' is-disabled' : ''}`} ref={rootRef}>
      <label htmlFor={inputId}>{label}{required ? ' *' : ''}</label>
      <div className="searchable-select-control">
        <input
          id={inputId}
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-autocomplete="list"
          autoComplete="off"
          disabled={disabled}
          placeholder={placeholder}
          value={displayValue}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
        />
        {allowClear && value !== '' && !disabled && (
          <button
            type="button"
            className="searchable-select-clear"
            aria-label="مسح الاختيار"
            onClick={() => { onChange(''); setQuery(''); }}
          >
            ×
          </button>
        )}
      </div>
      {open && (
        <ul id={listboxId} className="searchable-select-list" role="listbox">
          {loading && <li className="searchable-select-empty">جاري التحميل...</li>}
          {!loading && filtered.length === 0 && (
            <li className="searchable-select-empty">لا توجد نتائج</li>
          )}
          {!loading && filtered.map((option, index) => (
            <li
              key={option.id}
              role="option"
              aria-selected={value === option.id}
              className={`searchable-select-option${index === highlightIndex ? ' is-highlighted' : ''}${value === option.id ? ' is-selected' : ''}${option.isActive === false ? ' is-inactive' : ''}`}
              onMouseEnter={() => setHighlightIndex(index)}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => selectOption(option)}
            >
              <span>{option.name}</span>
              {option.isActive === false && <span className="inactive-tag">غير نشط</span>}
              {option.subLabel && <span className="searchable-select-sublabel">{option.subLabel}</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
