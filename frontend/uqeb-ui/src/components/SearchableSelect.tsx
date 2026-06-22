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

function getDisplayValue(open: boolean, query: string, selected: SelectOption | null): string {
  if (open) return query;
  if (!selected) return '';
  const inactiveSuffix = selected.isActive === false ? ' (غير نشط)' : '';
  return `${selected.name}${inactiveSuffix}`;
}

function optionId(listboxId: string, optionId: number) {
  return `${listboxId}-option-${optionId}`;
}

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
}: Readonly<SearchableSelectProps>) {
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
    const timer = globalThis.setTimeout(() => onSearch(query.trim()), debounceMs);
    return () => globalThis.clearTimeout(timer);
  }, [query, onSearch, debounceMs]);

  const clampedHighlight = Math.min(highlightIndex, Math.max(filtered.length - 1, 0));
  const activeOption = filtered[clampedHighlight] ?? null;
  const activeDescendantId = activeOption ? optionId(listboxId, activeOption.id) : undefined;

  useEffect(() => {
    const onDocClick = (e: MouseEvent) => {
      const target = e.target;
      if (target instanceof Node && !rootRef.current?.contains(target)) setOpen(false);
    };
    document.addEventListener('mousedown', onDocClick);
    return () => document.removeEventListener('mousedown', onDocClick);
  }, []);

  const displayValue = getDisplayValue(open, query, selected);

  const selectOption = useCallback((option: SelectOption) => {
    onChange(option.id);
    setQuery('');
    setOpen(false);
  }, [onChange]);

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!open && (e.key === 'ArrowDown' || e.key === 'Enter')) {
      setHighlightIndex(0);
      setOpen(true);
      return;
    }
    if (e.key === 'Escape') {
      setOpen(false);
      setQuery('');
      setHighlightIndex(0);
      return;
    }
    if (!open) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setHighlightIndex((i) => Math.min(i + 1, Math.max(filtered.length - 1, 0)));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHighlightIndex((i) => Math.max(i - 1, 0));
    } else if (e.key === 'Enter' && activeOption) {
      e.preventDefault();
      selectOption(activeOption);
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
          aria-activedescendant={open ? activeDescendantId : undefined}
          autoComplete="off"
          disabled={disabled}
          placeholder={placeholder}
          value={displayValue}
          onChange={(e) => {
            setQuery(e.target.value);
            setHighlightIndex(0);
            setOpen(true);
          }}
          onFocus={() => {
            setHighlightIndex(0);
            setOpen(true);
          }}
          onKeyDown={onKeyDown}
        />
        {allowClear && value !== '' && !disabled && (
          <button
            type="button"
            className="searchable-select-clear"
            aria-label="مسح الاختيار"
            onClick={() => { onChange(''); setQuery(''); setHighlightIndex(0); }}
          >
            ×
          </button>
        )}
      </div>
      {open && (
        <div id={listboxId} className="searchable-select-list" role="listbox" aria-label={label}>
          {loading && <div className="searchable-select-empty">جاري التحميل...</div>}
          {!loading && filtered.length === 0 && (
            <div className="searchable-select-empty">لا توجد نتائج</div>
          )}
          {!loading && filtered.map((option, index) => (
            <button
              key={option.id}
              id={optionId(listboxId, option.id)}
              type="button"
              role="option"
              aria-selected={value === option.id}
              className={`searchable-select-option${index === clampedHighlight ? ' is-highlighted' : ''}${value === option.id ? ' is-selected' : ''}${option.isActive === false ? ' is-inactive' : ''}`}
              onMouseEnter={() => setHighlightIndex(index)}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => selectOption(option)}
            >
              <span>{option.name}</span>
              {option.isActive === false && <span className="inactive-tag">غير نشط</span>}
              {option.subLabel && <span className="searchable-select-sublabel">{option.subLabel}</span>}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
