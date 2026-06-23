export function isInstitutionalReportsEnabled(): boolean {
  return import.meta.env.VITE_ENABLE_INSTITUTIONAL_REPORTS === 'true';
}
