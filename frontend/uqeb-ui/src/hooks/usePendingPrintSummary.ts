import { useContext } from 'react';
import { PendingPrintSummaryContext } from '../context/pendingPrintSummaryContextValue';

export function usePendingPrintSummary(enabled = true) {
  const context = useContext(PendingPrintSummaryContext);
  if (!enabled) {
    return {
      pendingTotal: 0,
      summary: null,
      loading: false,
      error: '',
      refresh: context.refresh,
    };
  }

  return {
    pendingTotal: context.summary?.total ?? 0,
    summary: context.summary,
    loading: context.loading,
    error: context.error,
    refresh: context.refresh,
  };
}
