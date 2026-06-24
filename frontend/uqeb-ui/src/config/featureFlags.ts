export function isInstitutionalReportsEnabled(): boolean {
  const value = import.meta.env.VITE_ENABLE_INSTITUTIONAL_REPORTS?.trim().toLowerCase();

  if (value === 'false')
    return false;

  return true;
}
