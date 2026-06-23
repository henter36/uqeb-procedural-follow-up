import { createContext } from 'react';
import type { Category, Department, ExternalParty } from '../api/types';

export type ReferenceDataState = {
  departments: Department[];
  categories: Category[];
  parties: ExternalParty[];
  loading: boolean;
  error: string | null;
};

export type ReferenceDataContextValue = ReferenceDataState & {
  refresh: () => void;
};

export const ReferenceDataContext = createContext<ReferenceDataContextValue | null>(null);
