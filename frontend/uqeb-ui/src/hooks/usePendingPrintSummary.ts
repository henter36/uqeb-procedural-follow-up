import { useCallback, useEffect, useState } from 'react';
import { followUpPrintApi } from '../api/services';
import { useAuth } from '../context/useAuth';

const POLL_INTERVAL_MS = 60_000;

export function usePendingPrintSummary(enabled = true) {
  const { canClose } = useAuth();
  const [pendingTotal, setPendingTotal] = useState(0);

  const refresh = useCallback(async () => {
    if (!enabled || !canClose) {
      setPendingTotal(0);
      return;
    }
    try {
      const res = await followUpPrintApi.getPendingSummary();
      setPendingTotal(res.data.total);
    } catch {
      // ignore polling errors
    }
  }, [canClose, enabled]);

  useEffect(() => {
    void refresh();
    if (!enabled || !canClose) return undefined;
    const timer = window.setInterval(() => { void refresh(); }, POLL_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [canClose, enabled, refresh]);

  return { pendingTotal, refresh };
}
