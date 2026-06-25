import { useCallback, useEffect, useState } from 'react';
import { followUpPrintApi } from '../api/services';
import { useAuth } from '../context/useAuth';

const POLL_INTERVAL_MS = 60_000;

export function usePendingPrintSummary(enabled = true) {
  const { canClose } = useAuth();
  const [pendingTotal, setPendingTotal] = useState(0);

  const fetchSummary = useCallback(async (active: () => boolean) => {
    try {
      const res = await followUpPrintApi.getPendingSummary();
      if (active()) setPendingTotal(res.data.total);
    } catch {
      // ignore polling errors
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    const active = () => !cancelled;

    (async () => {
      if (!enabled || !canClose) {
        await Promise.resolve();
        if (active()) setPendingTotal(0);
        return;
      }
      await fetchSummary(active);
    })();

    if (!enabled || !canClose) {
      return () => {
        cancelled = true;
      };
    }

    const timer = window.setInterval(() => {
      void fetchSummary(active);
    }, POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [canClose, enabled, fetchSummary]);

  return { pendingTotal };
}
