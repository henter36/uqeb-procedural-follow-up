import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';

type RequestState<T> = {
  data: T | null;
  loading: boolean;
  error: string | null;
};

export function useAbortableRequest<T>(
  requestKey: string,
  fetcher: (signal: AbortSignal) => Promise<T>,
  enabled = true,
): RequestState<T> & { retry: () => void } {
  const [state, setState] = useState<RequestState<T>>({ data: null, loading: enabled, error: null });
  const [retryKey, setRetryKey] = useState(0);
  const requestIdRef = useRef(0);
  const fetcherRef = useRef(fetcher);
  useLayoutEffect(() => {
    fetcherRef.current = fetcher;
  });

  const retry = useCallback(() => setRetryKey((k) => k + 1), []);

  useEffect(() => {
    if (!enabled) {
      setState({ data: null, loading: false, error: null });
      return undefined;
    }

    const controller = new AbortController();
    const requestId = ++requestIdRef.current;
    setState((prev) => ({ ...prev, loading: true, error: null }));

    fetcherRef.current(controller.signal)
      .then((data) => {
        if (controller.signal.aborted || requestId !== requestIdRef.current) return;
        setState({ data, loading: false, error: null });
      })
      .catch(() => {
        if (controller.signal.aborted || requestId !== requestIdRef.current) return;
        setState({ data: null, loading: false, error: 'تعذر تحميل البيانات' });
      });

    return () => {
      controller.abort();
    };
  }, [requestKey, enabled, retryKey]);

  return { ...state, retry };
}
