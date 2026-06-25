import { useEffect } from 'react';

/**
 * Runs an async effect without synchronously calling setState in the effect body.
 * Loading flags should start true; clear them inside `run` after awaits.
 */
export function useDeferredEffect(run: (active: () => boolean) => Promise<void>, deps: readonly unknown[]) {
  useEffect(() => {
    let cancelled = false;
    const active = () => !cancelled;
    void run(active);
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- deps are forwarded from callers
  }, deps);
}
