import type { InstitutionalReportManifest, ReportExportRequest } from '../api/services';
import { DetailOverflowAction, ExportFormat, ExportMode } from '../api/institutionalReports.constants';
import { addDaysIso, todayLocalIso } from '../utils/localDate';

export type PageSelectionMode = 'thumbnails' | 'range';

export const exportFormatLabels: Record<typeof ExportFormat[keyof typeof ExportFormat], string> = {
  [ExportFormat.Pdf]: 'PDF',
  [ExportFormat.Docx]: 'Word (DOCX)',
  [ExportFormat.Xlsx]: 'Excel (XLSX)',
  [ExportFormat.Html]: 'HTML',
};

/** Default YYYY-MM-DD for date inputs using local calendar parts (not UTC). */
export function defaultDate(offsetDays: number): string {
  return addDaysIso(todayLocalIso(), offsetDays);
}

export function buildReportExportPageSelection(
  exportMode: typeof ExportMode[keyof typeof ExportMode],
  pageSelectionMode: PageSelectionMode,
  selectedPages: number[],
  pageRange: string,
  currentPage: number,
): Pick<ReportExportRequest, 'selectedPageNumbers' | 'pageRangeExpression' | 'currentPageNumber'> {
  if (exportMode !== ExportMode.SelectedPages) {
    return {
      selectedPageNumbers: [],
      pageRangeExpression: null,
      currentPageNumber: exportMode === ExportMode.CurrentPage ? currentPage : null,
    };
  }

  if (pageSelectionMode === 'range' && pageRange.trim()) {
    return {
      selectedPageNumbers: [],
      pageRangeExpression: pageRange.trim(),
      currentPageNumber: null,
    };
  }

  return {
    selectedPageNumbers: selectedPages,
    pageRangeExpression: null,
    currentPageNumber: null,
  };
}

export function getPreviewStatusMessage(loading: boolean, error: string, hasManifest: boolean): string {
  if (loading) {
    return 'جاري التوليد...';
  }
  if (error && !hasManifest) {
    return error;
  }
  if (!hasManifest) {
    return 'اضغط «معاينة التقرير» لعرض الصفحات.';
  }
  return '';
}

export function getPageSelectionSummary(
  pageSelectionMode: PageSelectionMode,
  selectedPages: number[],
  pageRange: string,
  totalPages: number,
): string {
  if (pageSelectionMode === 'thumbnails' && selectedPages.length > 0) {
    return `تم تحديد ${selectedPages.length} من ${totalPages} صفحات (تحديد مصغرات)`;
  }
  if (pageSelectionMode === 'range' && pageRange.trim()) {
    return `نطاق الصفحات النشط: ${pageRange.trim()}`;
  }
  return '';
}

export function resolveEffectiveOverflowAction(
  requiresOverflowChoice: boolean,
  detailOverflowAction: typeof DetailOverflowAction[keyof typeof DetailOverflowAction],
  exportFormat: typeof ExportFormat[keyof typeof ExportFormat],
  manifest: InstitutionalReportManifest | null,
): typeof DetailOverflowAction[keyof typeof DetailOverflowAction] {
  if (requiresOverflowChoice) {
    return detailOverflowAction;
  }
  if (exportFormat === ExportFormat.Xlsx && manifest?.requiresDetailOverflowAction) {
    return DetailOverflowAction.FullDetailsXlsx;
  }
  return DetailOverflowAction.None;
}

export function resolveExportFileExtension(
  contentType: string,
  exportFormat: typeof ExportFormat[keyof typeof ExportFormat],
): string {
  if (contentType.includes('zip')) {
    return 'zip';
  }
  if (contentType.includes('spreadsheetml')) {
    return 'xlsx';
  }
  if (exportFormat === ExportFormat.Docx) {
    return 'docx';
  }
  if (exportFormat === ExportFormat.Html) {
    return 'html';
  }
  return 'pdf';
}
