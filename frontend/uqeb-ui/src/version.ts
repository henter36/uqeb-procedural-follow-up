export interface FrontendVersionInfo {
  frontendVersion: string;
  frontendCommitSha: string;
  frontendBuildTimeUtc: string | null;
}

const DEFAULT_FRONTEND_VERSION = '0.0.0';
const LOCAL_COMMIT_VALUE = 'local';
const SHORT_COMMIT_LENGTH = 7;

function normalizeValue(value: string | undefined): string | null {
  return value?.trim() || null;
}

export function normalizeCommitSha(value: string | undefined): string {
  const trimmed = normalizeValue(value);
  if (trimmed?.toLowerCase() === LOCAL_COMMIT_VALUE) return LOCAL_COMMIT_VALUE;
  return trimmed && /^[a-fA-F0-9]{7,40}$/.test(trimmed)
    ? trimmed.slice(0, SHORT_COMMIT_LENGTH).toLowerCase()
    : LOCAL_COMMIT_VALUE;
}

export const frontendVersionInfo: FrontendVersionInfo = {
  frontendVersion: normalizeValue(import.meta.env.VITE_APP_VERSION) ?? DEFAULT_FRONTEND_VERSION,
  frontendCommitSha: normalizeCommitSha(import.meta.env.VITE_COMMIT_SHA),
  frontendBuildTimeUtc: normalizeValue(import.meta.env.VITE_BUILD_TIME_UTC),
};
