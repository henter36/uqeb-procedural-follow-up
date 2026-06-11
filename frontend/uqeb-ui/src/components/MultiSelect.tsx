interface Option { id: number; name: string }

interface Props {
  options: Option[];
  selected: number[];
  onChange: (ids: number[]) => void;
  label?: string;
  required?: boolean;
}

export default function MultiSelect({ options, selected, onChange, label, required }: Props) {
  const toggle = (id: number) => {
    onChange(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);
  };

  return (
    <div className="form-group">
      {label && <label>{label}{required ? ' *' : ''}</label>}
      <div className="multi-select">
        {options.map((o) => (
          <label key={o.id} className="multi-select-item">
            <input type="checkbox" checked={selected.includes(o.id)} onChange={() => toggle(o.id)} />
            {o.name}
          </label>
        ))}
        {options.length === 0 && <span className="text-muted">لا توجد خيارات</span>}
      </div>
    </div>
  );
}
