import { useEffect, useRef, useState } from 'react';
import { systemApi } from '../../api/services';
import type { SystemVersionInfo } from '../../api/types';
import { frontendVersionInfo, type FrontendVersionInfo } from '../../version';

type AppVersionInfoProps = Readonly<{
  frontendInfo?: FrontendVersionInfo;
  loadBackendVersion?: () => Promise<SystemVersionInfo>;
  initiallyOpen?: boolean;
}>;

type BackendVersionState =
  | { status: 'idle' | 'loading' }
  | { status: 'success'; data: SystemVersionInfo }
  | { status: 'error' };

const defaultLoadBackendVersion = async () => {
  const response = await systemApi.getVersion();
  return response.data;
};

function formatBuildTime(value: string | null | undefined): string {
  if (!value) return 'غير متاح';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'غير متاح';

  return `${new Intl.DateTimeFormat('ar-SA', {
    calendar: 'gregory',
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'UTC',
  }).format(date)} UTC`;
}

export default function AppVersionInfo({
  frontendInfo = frontendVersionInfo,
  loadBackendVersion = defaultLoadBackendVersion,
  initiallyOpen = false,
}: AppVersionInfoProps) {
  const [open, setOpen] = useState(initiallyOpen);
  const [backendVersion, setBackendVersion] = useState<BackendVersionState>({ status: 'idle' });
  const requestedBackendVersion = useRef(false);

  useEffect(() => {
    if (!open || requestedBackendVersion.current) return;

    let active = true;
    requestedBackendVersion.current = true;
    setBackendVersion({ status: 'loading' });

    loadBackendVersion()
      .then((data) => {
        if (active) setBackendVersion({ status: 'success', data });
      })
      .catch(() => {
        if (active) {
          setBackendVersion({ status: 'error' });
          requestedBackendVersion.current = false;
        }
      });

    return () => {
      active = false;
    };
  }, [loadBackendVersion, open]);

  return (
    <div className="app-version-info">
      <button
        type="button"
        className="app-version-info-button"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        aria-controls="app-version-info-panel"
      >
        معلومات الإصدار
      </button>

      {open && (
        <output id="app-version-info-panel" className="app-version-info-panel" aria-live="polite">
          <p>
            الواجهة: v{frontendInfo.frontendVersion} - commit {frontendInfo.frontendCommitSha} - built{' '}
            {formatBuildTime(frontendInfo.frontendBuildTimeUtc)}
          </p>

          {backendVersion.status === 'loading' && <p>جاري قراءة إصدار الخادم...</p>}
          {backendVersion.status === 'error' && <p>تعذر قراءة إصدار الخادم.</p>}
          {backendVersion.status === 'success' && (
            <>
              <p>
                الخادم: v{backendVersion.data.backendVersion} - commit {backendVersion.data.backendCommitSha} - built{' '}
                {formatBuildTime(backendVersion.data.backendBuildTimeUtc)}
              </p>
              <p>البيئة: {backendVersion.data.environment}</p>
            </>
          )}
        </output>
      )}
    </div>
  );
}
