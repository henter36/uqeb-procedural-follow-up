import {
  useCallback, useEffect, useMemo, useRef, useState, type ReactNode,
} from 'react';
import { followUpPrintApi } from '../api/services';
import type { FollowUpPrintPendingSummary } from '../api/types';
import { useAuth } from './useAuth';
import { getApiErrorMessage } from '../utils/apiHelpers';
import { PendingPrintSummaryContext } from './pendingPrintSummaryContextValue';

const POLL_INTERVAL_MS = 60_000;

export function PendingPrintSummaryProvider({ children }: { children: ReactNode }) {
  const { canClose, user } = useAuth();
  const [summary, setSummary] = useState<FollowUpPrintPendingSummary | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const requestSeq = useRef(0);

  const refresh = useCallback(async () => {
    if (!canClose || !user) {
      setSummary(null);
      setError('');
      setLoading(false);
      return;
    }

    const seq = requestSeq.current + 1;
    requestSeq.current = seq;
    setLoading(true);
    try {
      const res = await followUpPrintApi.getPendingSummary();
      if (requestSeq.current !== seq) return;
      setSummary(res.data);
      setError('');
    } catch (err: unknown) {
      if (requestSeq.current !== seq) return;
      setError(getApiErrorMessage(err));
    } finally {
      if (requestSeq.current === seq) setLoading(false);
    }
  }, [canClose, user]);

  useEffect(() => {
    let mounted = true;

    if (!canClose || !user) {
      requestSeq.current += 1;
      Promise.resolve().then(() => {
        setSummary(null);
        setError('');
        setLoading(false);
      });
      return () => {
        mounted = false;
      };
    }

    Promise.resolve().then(() => {
      if (mounted) refresh().catch(() => undefined);
    });
    const timer = globalThis.setInterval(() => {
      if (mounted) refresh().catch(() => undefined);
    }, POLL_INTERVAL_MS);

    return () => {
      mounted = false;
      requestSeq.current += 1;
      globalThis.clearInterval(timer);
    };
  }, [canClose, refresh, user]);

  const value = useMemo(() => ({
    summary,
    loading,
    error,
    refresh,
  }), [error, loading, refresh, summary]);

  return (
    <PendingPrintSummaryContext.Provider value={value}>
      {children}
    </PendingPrintSummaryContext.Provider>
  );
}
