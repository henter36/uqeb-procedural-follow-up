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
  const listboxId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const selectRef = useRef<HTMLSelectElement>(null);
  const lastCommittedIdRef = useRef<number | null>(null);
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
  const visibleOptionCount = Math.min(Math.max(filtered.length, 1), 6);

  useEffect(() => {
    if (!open || !activeOption || !selectRef.current) return;
    const optionEl = selectRef.current.querySelector(`option[value="${activeOption.id}"]`);
    optionEl?.scrollIntoView?.({ block: 'nearest' });
  }, [open, activeOption]);

  useEffect(() => {
    const onDocPointerDown = (e: PointerEvent) => {
      const target = e.target;
      if (target instanceof Node && !rootRef.current?.contains(target)) {
        setOpen(false);
        setQuery('');
        setHighlightIndex(0);
      }
    };
    document.addEventListener('pointerdown', onDocPointerDown);
    return () => document.removeEventListener('pointerdown', onDocPointerDown);
  }, []);

  const displayValue = getDisplayValue(open, query, selected);

  const selectOption = useCallback((option: SelectOption) => {
    if (value !== option.id) {
      onChange(option.id);
    }
    setQuery('');
    setOpen(false);
    setHighlightIndex(0);
  }, [onChange, value]);

  const closeList = useCallback(() => {
    setOpen(false);
    setQuery('');
    setHighlightIndex(0);
  }, []);

  const commitNativeSelection = useCallback((selectedId: number) => {
    if (!Number.isFinite(selectedId)) return;
    if (lastCommittedIdRef.current === selectedId) return;

    const option = filtered.find((item) => item.id === selectedId);
    if (!option) return;

    lastCommittedIdRef.current = selectedId;
    selectOption(option);
    globalThis.queueMicrotask(() => {
      lastCommittedIdRef.current = null;
    });
  }, [filtered, selectOption]);

  const onInputKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!open && (e.key === 'ArrowDown' || e.key === 'Enter')) {
      e.preventDefault();
      setHighlightIndex(0);
      setOpen(true);
      return;
    }
    if (e.key === 'Escape') {
      e.preventDefault();
      closeList();
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
          ref={inputRef}
          id={inputId}
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={open ? listboxId : undefined}
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
          onBlur={() => {
            globalThis.setTimeout(() => {
              if (!rootRef.current?.contains(document.activeElement)) {
                closeList();
              }
            }, 0);
          }}
          onKeyDown={onInputKeyDown}
        />
        {allowClear && value !== '' && !disabled && (
          <button
            type="button"
            className="searchable-select-clear"
            aria-label="مسح الاختيار"
            onMouseDown={(e) => e.preventDefault()}
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
              id={listboxId}
              ref={selectRef}
              tabIndex={-1}
              className="searchable-select-list"
              size={visibleOptionCount}
              aria-label={`${label} - النتائج`}
              value={activeOption ? String(activeOption.id) : ''}
              onPointerDown={(event) => {
                event.preventDefault();
              }}
              onChange={(event) => {
                commitNativeSelection(Number(event.currentTarget.value));
              }}
              onClick={(event) => {
                const target = event.target;
                if (target instanceof HTMLOptionElement) {
                  commitNativeSelection(Number(target.value));
                }
              }}
              onMouseMove={(event) => {
                const target = event.target;
                if (!(target instanceof HTMLOptionElement)) return;

                const index = filtered.findIndex((item) => item.id === Number(target.value));
                if (index >= 0) {
                  setHighlightIndex(index);
                }
              }}
              onKeyDown={(event) => {
                if (event.key === 'Escape') {
                  event.preventDefault();
                  closeList();
                  inputRef.current?.focus();
                  return;
                }

                if (event.key === 'Enter') {
                  event.preventDefault();
                  commitNativeSelection(Number(event.currentTarget.value));
                  inputRef.current?.focus();
                }
              }}
            >
              {filtered.map((option) => (
                <option
                  key={option.id}
                  value={option.id}
                  className={
                    option.isActive === false
                      ? 'searchable-select-option is-inactive'
                      : 'searchable-select-option'
                  }
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
