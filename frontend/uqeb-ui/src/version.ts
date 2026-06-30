export interface FrontendVersionInfo {
  frontendVersion: string;
  frontendCommitSha: string;
  frontendBuildTimeUtc: string | null;
}

const DEFAULT_FRONTEND_VERSION = '0.0.0';

function normalizeValue(value: string | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

function normalizeCommitSha(value: string | undefined): string {
  const trimmed = normalizeValue(value);
  if (!trimmed || trimmed.toLowerCase() === 'local') return 'local';
  return /^[a-fA-F0-9]{7,40}$/.test(trimmed) ? trimmed.slice(0, 7).toLowerCase() : 'local';
}

export const frontendVersionInfo: FrontendVersionInfo = {
  frontendVersion: normalizeValue(import.meta.env.VITE_APP_VERSION) ?? DEFAULT_FRONTEND_VERSION,
  frontendCommitSha: normalizeCommitSha(import.meta.env.VITE_COMMIT_SHA),
  frontendBuildTimeUtc: normalizeValue(import.meta.env.VITE_BUILD_TIME_UTC),
};
