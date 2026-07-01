import { useEffect, useId, useMemo, useRef, useState } from 'react';

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
  if (count === 2) return 'إدارتان مختارتان';
  if (count >= 3 && count <= 10) return `${count} إدارات مختارة`;
  return `${count} إدارة مختارة`;
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
  const containerRef = useRef<HTMLDivElement>(null);
  const generatedPanelId = useId();

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
  const panelId = dataFieldName ? `${dataFieldName}-options` : generatedPanelId;

  useEffect(() => {
    if (!isOpen) return undefined;

    const handleClickOutside = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setIsOpen(false);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen]);

  return (
    <div className="form-group multi-select-container" ref={containerRef}>
      {label && <label>{label}{required ? ' *' : ''}</label>}
      <button
        type="button"
        className={`multi-select-trigger${invalid ? ' is-invalid' : ''}`}
        onClick={() => setIsOpen((open) => !open)}
        aria-expanded={isOpen}
        aria-controls={isOpen ? panelId : undefined}
        data-field-name={dataFieldName}
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
        <div id={panelId} className="multi-select-panel">
          {showSearch && (
            <input
              className="multi-select-search"
              placeholder="بحث..."
              aria-label="بحث في الإدارات"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          )}
          <div className="multi-select">
            {filtered.map((o) => (
              <label key={o.id} className={`multi-select-item${o.isActive === false ? ' is-inactive' : ''}`}>
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
