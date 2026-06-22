import { Link } from 'react-router-dom';

type StatCardProps = {
  label: string;
  value: string | number;
  color?: string;
  link?: string;
  loading?: boolean;
};

export default function StatCard({ label, value, color = 'blue', link, loading }: StatCardProps) {
  const content = (
    <>
      <div className="stat-value">{loading ? '—' : value}</div>
      <div className="stat-label">{label}</div>
    </>
  );

  if (link) {
    return (
      <Link to={link} className={`stat-card stat-${color}`}>
        {content}
      </Link>
    );
  }

  return <div className={`stat-card stat-${color}`}>{content}</div>;
}
