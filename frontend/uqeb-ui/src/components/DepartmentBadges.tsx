export default function DepartmentBadges({ names }: { names?: string[] }) {
  if (!names || names.length === 0) return <span>-</span>;
  return (
    <span className="dept-badges">
      {names.map((name) => (
        <span key={name} className="badge badge-blue">{name}</span>
      ))}
    </span>
  );
}
