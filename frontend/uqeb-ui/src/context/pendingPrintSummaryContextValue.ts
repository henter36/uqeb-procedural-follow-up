import { createContext } from 'react';
import type { FollowUpPrintPendingSummary } from '../api/types';

export type PendingPrintSummaryContextValue = {
  summary: FollowUpPrintPendingSummary | null;
  loading: boolean;
  error: string;
  refresh: () => Promise<void>;
};

export const pendingPrintSummaryDefaultValue: PendingPrintSummaryContextValue = {
  summary: null,
  loading: false,
  error: '',
  refresh: async () => {},
};

export const PendingPrintSummaryContext = createContext<PendingPrintSummaryContextValue>(
  pendingPrintSummaryDefaultValue,
);
