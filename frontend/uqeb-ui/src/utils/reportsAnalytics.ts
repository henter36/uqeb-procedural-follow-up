export function getAnalyticsStatusText(
  loading: boolean,
  updatedAt: Date | null,
  loaded: boolean,
): string {
  if (loading) {
    return 'جاري تحديث التحليلات...';
  }

  if (updatedAt) {
    return `آخر تحديث: ${updatedAt.toLocaleString('ar-SA')}`;
  }

  if (loaded) {
    return 'تم تحميل التحليلات.';
  }

  return 'لم يتم تحميل التحليلات بعد.';
}

export type AnalyticsViewState = 'loading-initial' | 'loading-refresh' | 'empty' | 'content';

export function getAnalyticsViewState(
  loading: boolean,
  loaded: boolean,
): AnalyticsViewState {
  if (loading && loaded) {
    return 'loading-refresh';
  }
  if (loading) {
    return 'loading-initial';
  }
  if (loaded) {
    return 'content';
  }
  return 'empty';
}
