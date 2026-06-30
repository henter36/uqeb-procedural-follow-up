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

  const showSearch = options.length > 6;

  const filtered = useMemo(() => {
    if (!showSearch) return options;
    const term = query.trim().toLowerCase();
    if (!term) return options;
    return options.filter((o) => o.name.toLowerCase().includes(term));
  }, [options, query, showSearch]);

  const toggle = (id: number) => {
    onChange(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);
  };

  return (
    <div className="form-group">
      {label && <label>{label}{required ? ' *' : ''}</label>}
      {showSearch && (
        <input
          className="multi-select-search"
          placeholder="بحث..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      )}
      <div
        className="multi-select"
        tabIndex={dataFieldName ? -1 : undefined}
        data-field-name={dataFieldName}
        aria-invalid={invalid ? true : undefined}
        aria-describedby={describedBy}
      >
        {filtered.map((o) => (
          <label key={o.id} className={`multi-select-item${o.isActive === false ? ' is-inactive' : ''}`}>
            <input type="checkbox" checked={selected.includes(o.id)} onChange={() => toggle(o.id)} />
            {o.name}{o.isActive === false ? ' (غير نشط)' : ''}
          </label>
        ))}
        {filtered.length === 0 && <span className="text-muted">لا توجد نتائج</span>}
      </div>
    </div>
  );
}
