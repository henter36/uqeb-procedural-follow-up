import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { categoriesApi, departmentsApi, externalPartiesApi } from '../api/services';
import {
  ReferenceDataContext,
  type ReferenceDataContextValue,
  type ReferenceDataState,
} from './referenceDataStore';

const EMPTY_STATE: ReferenceDataState = {
  departments: [],
  categories: [],
  parties: [],
  loading: true,
  error: null,
};

async function fetchReferenceData(): Promise<ReferenceDataState> {
  try {
    const [departments, categories, parties] = await Promise.all([
      departmentsApi.getAll(),
      categoriesApi.getAll(),
      externalPartiesApi.getAll(),
    ]);
    return {
      departments: departments.data,
      categories: categories.data,
      parties: parties.data,
      loading: false,
      error: null,
    };
  } catch {
    return {
      departments: [],
      categories: [],
      parties: [],
      loading: false,
      error: 'تعذر تحميل البيانات المرجعية',
    };
  }
}

export function ReferenceDataProvider({ children }: Readonly<{ children: ReactNode }>) {
  const [data, setData] = useState<ReferenceDataState>(EMPTY_STATE);
  const mountedRef = useRef(true);
  const cacheRef = useRef<ReferenceDataState | null>(null);
  const requestRef = useRef<Promise<ReferenceDataState> | null>(null);
  const generationRef = useRef(0);

  const applyData = useCallback((generation: number, next: ReferenceDataState) => {
    if (!mountedRef.current || generation !== generationRef.current) return;
    cacheRef.current = next;
    setData(next);
  }, []);

  const loadData = useCallback(async (generation: number) => {
    requestRef.current ??= fetchReferenceData();
    try {
      const next = await requestRef.current;
      requestRef.current = null;
      applyData(generation, next);
    } catch {
      requestRef.current = null;
      applyData(generation, {
        ...EMPTY_STATE,
        loading: false,
        error: 'تعذر تحميل البيانات المرجعية',
      });
    }
  }, [applyData]);

  const refresh = useCallback(() => {
    generationRef.current += 1;
    cacheRef.current = null;
    requestRef.current = null;
    setData((prev) => ({ ...prev, loading: true, error: null }));
    void loadData(generationRef.current);
  }, [loadData]);

  useEffect(() => {
    mountedRef.current = true;
    const generation = generationRef.current;

    if (cacheRef.current) {
      setData(cacheRef.current);
      return () => {
        mountedRef.current = false;
        generationRef.current += 1;
        requestRef.current = null;
      };
    }

    void loadData(generation);
    return () => {
      mountedRef.current = false;
      generationRef.current += 1;
      requestRef.current = null;
    };
  }, [loadData]);

  const value = useMemo<ReferenceDataContextValue>(() => ({
    ...data,
    refresh,
  }), [data, refresh]);

  return (
    <ReferenceDataContext.Provider value={value}>
      {children}
    </ReferenceDataContext.Provider>
  );
}
