import { isInstitutionalReportsEnabled } from './featureFlags';

export function institutionalReportsEnabled(): boolean {
  return isInstitutionalReportsEnabled();
}
