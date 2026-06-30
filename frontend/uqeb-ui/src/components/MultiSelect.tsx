import { useMemo, useState } from 'react';

type Option = Readonly<{ id: number; name: string; isActive?: boolean }>;

type Props = Readonly<{
  options: readonly Option[];
  selected: readonly number[];
  onChange: (ids: number[]) => void;
  label?: string;
  required?: boolean;
  invalid?: boolean;
  describedBy?: string;
  dataFieldName?: string;
}>;

function formatSelectedCount(count: number) {
  if (count === 0) return 'لم يتم اختيار أي إدارة';
  if (count === 1) return 'إدارة واحدة مختارة';
  return `${count} إدارات مختارة`;
}

export default function MultiSelect({
  options,
  selected,
  onChange,
  label,
  required,
  invalid,
  describedBy,
  dataFieldName,
}: Props) {
  const [query, setQuery] = useState('');
  const [isOpen, setIsOpen] = useState(false);

  const showSearch = options.length > 6;
  const selectedOptions = useMemo(
    () => options.filter((option) => selected.includes(option.id)),
    [options, selected],
  );

  const filtered = useMemo(() => {
    if (!showSearch) return options;
    const term = query.trim().toLowerCase();
    if (!term) return options;
    return options.filter((o) => o.name.toLowerCase().includes(term));
  }, [options, query, showSearch]);

  const toggle = (id: number) => {
    onChange(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);
  };
  const remove = (id: number) => onChange(selected.filter((x) => x !== id));
  const selectedCountLabel = formatSelectedCount(selected.length);
  const listboxId = dataFieldName ? `${dataFieldName}-options` : undefined;

  return (
    <div className="form-group">
      {label && <label>{label}{required ? ' *' : ''}</label>}
      <button
        type="button"
        className="multi-select-trigger"
        onClick={() => setIsOpen((open) => !open)}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        aria-controls={listboxId}
        data-field-name={dataFieldName}
        aria-invalid={invalid ? true : undefined}
        aria-describedby={describedBy}
      >
        <span>{selectedCountLabel}</span>
        <span aria-hidden="true">▾</span>
      </button>
      {selectedOptions.length > 0 && (
        <div className="multi-select-chips" aria-label="الإدارات المختارة">
          {selectedOptions.map((option) => (
            <button
              key={option.id}
              type="button"
              className="multi-select-chip"
              onClick={() => remove(option.id)}
              aria-label={`إزالة ${option.name}`}
            >
                <span className="multi-select-chip-text" title={option.name}>{option.name}</span>
                <span aria-hidden="true">×</span>
              </button>
          ))}
        </div>
      )}
      {isOpen && (
        <div className="multi-select-panel">
          {showSearch && (
            <input
              className="multi-select-search"
              placeholder="بحث..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          )}
          <div id={listboxId} className="multi-select" role="listbox" aria-multiselectable="true">
            {filtered.map((o) => (
              <label
                key={o.id}
                className={`multi-select-item${o.isActive === false ? ' is-inactive' : ''}`}
                role="option"
                aria-selected={selected.includes(o.id)}
              >
                <input
                  className="multi-select-checkbox"
                  type="checkbox"
                  checked={selected.includes(o.id)}
                  onChange={() => toggle(o.id)}
                />
                <span className="multi-select-item-text" title={o.name}>
                  {o.name}{o.isActive === false ? ' (غير نشط)' : ''}
                </span>
              </label>
            ))}
            {filtered.length === 0 && <span className="text-muted">لا توجد نتائج</span>}
          </div>
        </div>
      )}
    </div>
  );
}
