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

const MAX_VISIBLE_OPTIONS = 6;

function getDisplayValue(open: boolean, query: string, selected: SelectOption | null): string {
  if (open) return query;
  if (!selected) return '';
  const inactiveSuffix = selected.isActive === false ? ' (غير نشط)' : '';
  return `${selected.name}${inactiveSuffix}`;
}

function formatOptionLabel(option: SelectOption): string {
  const inactiveSuffix = option.isActive === false ? ' (غير نشط)' : '';
  const subLabelSuffix = option.subLabel ? ` — ${option.subLabel}` : '';
  return `${option.name}${inactiveSuffix}${subLabelSuffix}`;
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
  const selectListId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const selectRef = useRef<HTMLSelectElement>(null);
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

  useEffect(() => {
    if (!open || !selectRef.current || filtered.length === 0) return;
    selectRef.current.selectedIndex = clampedHighlight;
  }, [open, clampedHighlight, filtered.length]);

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

  const onInputKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!open && (e.key === 'ArrowDown' || e.key === 'Enter')) {
      e.preventDefault();
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

  const onSelectKeyDown = (e: React.KeyboardEvent<HTMLSelectElement>) => {
    if (e.key === 'Escape') {
      e.preventDefault();
      setOpen(false);
      setQuery('');
      setHighlightIndex(0);
      return;
    }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      const nextIndex = e.currentTarget.selectedIndex;
      setHighlightIndex(nextIndex);
      return;
    }
    if (e.key === 'Enter') {
      e.preventDefault();
      const option = filtered[e.currentTarget.selectedIndex];
      if (option) selectOption(option);
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
          aria-controls={open ? selectListId : undefined}
          aria-autocomplete="list"
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
          onKeyDown={onInputKeyDown}
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
        <div className="searchable-select-results">
          {loading && <div className="searchable-select-empty">جاري التحميل...</div>}
          {!loading && filtered.length === 0 && (
            <div className="searchable-select-empty">لا توجد نتائج</div>
          )}
          {!loading && filtered.length > 0 && (
            <select
              id={selectListId}
              ref={selectRef}
              className="searchable-select-list"
              size={Math.min(filtered.length, MAX_VISIBLE_OPTIONS)}
              aria-label={label}
              value={activeOption ? String(activeOption.id) : ''}
              onChange={(e) => {
                const id = Number(e.target.value);
                const option = filtered.find((o) => o.id === id);
                if (option) selectOption(option);
              }}
              onKeyDown={onSelectKeyDown}
              onMouseMove={(e) => {
                const target = e.target;
                if (!(target instanceof HTMLOptionElement)) return;
                const index = filtered.findIndex((o) => o.id === Number(target.value));
                if (index >= 0) setHighlightIndex(index);
              }}
            >
              {filtered.map((option) => (
                <option
                  key={option.id}
                  value={option.id}
                  className={`searchable-select-option${value === option.id ? ' is-selected' : ''}${option.isActive === false ? ' is-inactive' : ''}`}
                >
                  {formatOptionLabel(option)}
                </option>
              ))}
            </select>
          )}
        </div>
      )}
    </div>
  );
}
