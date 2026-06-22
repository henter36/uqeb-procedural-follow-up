import {
  createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode,
} from 'react';
import { categoriesApi, departmentsApi, externalPartiesApi } from '../api/services';
import type { Category, Department, ExternalParty } from '../api/types';

type ReferenceDataContextValue = {
  departments: Department[];
  categories: Category[];
  parties: ExternalParty[];
  loading: boolean;
  error: string | null;
  refresh: () => void;
};

const ReferenceDataContext = createContext<ReferenceDataContextValue | null>(null);

let sharedPromise: Promise<void> | null = null;
let sharedCache: Omit<ReferenceDataContextValue, 'refresh'> | null = null;

async function loadReferenceData(): Promise<Omit<ReferenceDataContextValue, 'refresh'>> {
  if (sharedCache) return sharedCache;
  if (!sharedPromise) {
    sharedPromise = Promise.all([
      departmentsApi.getAll(),
      categoriesApi.getAll(),
      externalPartiesApi.getAll(),
    ]).then(([departments, categories, parties]) => {
      sharedCache = {
        departments: departments.data,
        categories: categories.data,
        parties: parties.data,
        loading: false,
        error: null,
      };
    }).catch(() => {
      sharedCache = {
        departments: [],
        categories: [],
        parties: [],
        loading: false,
        error: 'تعذر تحميل البيانات المرجعية',
      };
    }).finally(() => {
      sharedPromise = null;
    });
  }
  await sharedPromise;
  return sharedCache!;
}

export function ReferenceDataProvider({ children }: Readonly<{ children: ReactNode }>) {
  const [data, setData] = useState<Omit<ReferenceDataContextValue, 'refresh'>>(() => (
    sharedCache ?? {
      departments: [],
      categories: [],
      parties: [],
      loading: true,
      error: null,
    }
  ));
  const mountedRef = useRef(true);

  const refresh = () => {
    sharedCache = null;
    setData((prev) => ({ ...prev, loading: true, error: null }));
    void loadReferenceData().then((next) => {
      if (mountedRef.current) setData(next);
    });
  };

  useEffect(() => {
    mountedRef.current = true;
    if (sharedCache) return undefined;
    let active = true;
    void loadReferenceData().then((next) => {
      if (active && mountedRef.current) setData(next);
    });
    return () => {
      active = false;
      mountedRef.current = false;
    };
  }, []);

  const value = useMemo<ReferenceDataContextValue>(() => ({
    ...data,
    refresh,
  }), [data]);

  return (
    <ReferenceDataContext.Provider value={value}>
      {children}
    </ReferenceDataContext.Provider>
  );
}

export function useReferenceData(): ReferenceDataContextValue {
  const ctx = useContext(ReferenceDataContext);
  if (!ctx) throw new Error('useReferenceData must be used within ReferenceDataProvider');
  return ctx;
}
