import { useCallback, useEffect, useRef, useState, type RefObject, type SyntheticEvent } from 'react';
import axios from 'axios';
import { institutionalReportsApi, type InstitutionalReportManifest, type ReportBuildRequest, type ReportExportRequest } from '../api/services';
import {
  DetailOverflowAction,
  ExportFormat,
  ExportMode,
  PageNumberingMode,
  ReportSectionId,
} from '../api/institutionalReports.constants';
import { getApiErrorDetails, parseApiErrorResponseData } from '../utils/apiHelpers';
import { downloadBlob } from '../utils/downloadBlob';
import {
  buildReportExportPageSelection,
  resolveEffectiveOverflowAction,
  resolveExportFileExtension,
  type PageSelectionMode,
} from './reportBuilderHelpers';

export type UseReportBuilderExportParams = {
  buildRequest: () => ReportBuildRequest;
  manifest: InstitutionalReportManifest | null;
  sectionIds: number[];
  selectedPages: number[];
  pageSelectionMode: PageSelectionMode;
  pageRange: string;
  currentPage: number;
  setLoading: (loading: boolean) => void;
  setError: (error: string) => void;
  setErrorCorrelationId?: (correlationId: string) => void;
};

export type UseReportBuilderExportResult = {
  exportOpen: boolean;
  openExportDialog: () => void;
  closeExportDialog: () => void;
  exportDialogRef: RefObject<HTMLDialogElement | null>;
  handleExportDialogCancel: (event: SyntheticEvent) => void;
  exportMode: typeof ExportMode[keyof typeof ExportMode];
  setExportMode: (mode: typeof ExportMode[keyof typeof ExportMode]) => void;
  exportFormat: typeof ExportFormat[keyof typeof ExportFormat];
  setExportFormat: (format: typeof ExportFormat[keyof typeof ExportFormat]) => void;
  pageNumberingMode: typeof PageNumberingMode[keyof typeof PageNumberingMode];
  setPageNumberingMode: (mode: typeof PageNumberingMode[keyof typeof PageNumberingMode]) => void;
  includePartialCover: boolean;
  setIncludePartialCover: (value: boolean) => void;
  includePartialManifest: boolean;
  setIncludePartialManifest: (value: boolean) => void;
  detailOverflowAction: typeof DetailOverflowAction[keyof typeof DetailOverflowAction];
  setDetailOverflowAction: (action: typeof DetailOverflowAction[keyof typeof DetailOverflowAction]) => void;
  requiresOverflowChoice: boolean;
  runExport: () => Promise<void>;
};

export function useReportBuilderExport({
  buildRequest,
  manifest,
  sectionIds,
  selectedPages,
  pageSelectionMode,
  pageRange,
  currentPage,
  setLoading,
  setError,
  setErrorCorrelationId,
}: UseReportBuilderExportParams): UseReportBuilderExportResult {
  const exportDialogRef = useRef<HTMLDialogElement>(null);
  const [exportDialogManifestId, setExportDialogManifestId] = useState<string | null>(null);
  const exportOpen = manifest !== null && exportDialogManifestId === manifest.reportId;
  const [exportMode, setExportMode] = useState<typeof ExportMode[keyof typeof ExportMode]>(ExportMode.FullReport);
  const [exportFormat, setExportFormat] = useState<typeof ExportFormat[keyof typeof ExportFormat]>(ExportFormat.Pdf);
  const [pageNumberingMode, setPageNumberingMode] = useState<typeof PageNumberingMode[keyof typeof PageNumberingMode]>(
    PageNumberingMode.Restart,
  );
  const [includePartialCover, setIncludePartialCover] = useState(true);
  const [includePartialManifest, setIncludePartialManifest] = useState(true);
  const [detailOverflowAction, setDetailOverflowAction] = useState<typeof DetailOverflowAction[keyof typeof DetailOverflowAction]>(
    DetailOverflowAction.None,
  );

  const includesTransactionDetails = sectionIds.includes(ReportSectionId.TransactionDetails);
  const requiresOverflowChoice = Boolean(
    manifest?.requiresDetailOverflowAction
    && includesTransactionDetails
    && (exportFormat === ExportFormat.Pdf || exportFormat === ExportFormat.Docx || exportFormat === ExportFormat.Html),
  );

  const openExportDialog = useCallback(() => {
    if (!manifest)
      return;

    setExportDialogManifestId(manifest.reportId);
  }, [manifest]);
  const closeExportDialog = useCallback(() => setExportDialogManifestId(null), []);

  const handleExportDialogCancel = useCallback((event: SyntheticEvent) => {
    event.preventDefault();
    closeExportDialog();
  }, [closeExportDialog]);

  useEffect(() => {
    const dialog = exportDialogRef.current;
    if (!dialog) {
      return;
    }
    if (exportOpen && !dialog.open) {
      dialog.showModal();
    } else if (!exportOpen && dialog.open) {
      dialog.close();
    }
  }, [exportOpen]);

  const runExport = useCallback(async () => {
    setLoading(true);
    setError('');
    setErrorCorrelationId?.('');
    try {
      if (requiresOverflowChoice && detailOverflowAction === DetailOverflowAction.None) {
        setError('يتجاوز التقرير حد صفوف التفاصيل. اختر كيفية التعامل مع التفاصيل قبل التصدير.');
        return;
      }

      if (requiresOverflowChoice && detailOverflowAction === DetailOverflowAction.SplitPdf && exportFormat !== ExportFormat.Pdf) {
        setError('تقسيم التفاصيل إلى عدة ملفات PDF متاح فقط عند اختيار صيغة PDF.');
        return;
      }

      const pageSelection = buildReportExportPageSelection(
        exportMode,
        pageSelectionMode,
        selectedPages,
        pageRange,
        currentPage,
      );

      const effectiveOverflowAction = resolveEffectiveOverflowAction(
        requiresOverflowChoice,
        detailOverflowAction,
        exportFormat,
        manifest,
      );

      const response = await institutionalReportsApi.export({
        buildRequest: buildRequest(),
        exportFormat,
        exportMode,
        selectedSectionIds: sectionIds as ReportExportRequest['selectedSectionIds'],
        includePartialCover,
        includePartialManifest,
        pageNumberingMode,
        detailOverflowAction: effectiveOverflowAction,
        ...pageSelection,
      });

      const contentType = typeof response.headers['content-type'] === 'string'
        ? response.headers['content-type']
        : 'application/octet-stream';
      const ext = resolveExportFileExtension(contentType, exportFormat);
      const blob = new Blob([response.data], { type: contentType });
      downloadBlob(blob, `institutional-report.${ext}`);
      closeExportDialog();
    } catch (err) {
      const axiosDetails = getApiErrorDetails(err);

      const blobDetails =
        axios.isAxiosError(err) && err.response?.data instanceof Blob
          ? await parseApiErrorResponseData(err.response.data)
          : null;

      const details = blobDetails
        ? {
            ...blobDetails,
            correlationId: blobDetails.correlationId || axiosDetails.correlationId,
            httpStatus: axiosDetails.httpStatus,
          }
        : axiosDetails;
      const overflowMessage = details.validationErrors.detailOverflowAction;

      if (overflowMessage) {
        setError(overflowMessage);
        setErrorCorrelationId?.(details.correlationId);
        return;
      }

      const defaultMessage = 'تعذر تصدير التقرير.';
      const backendMessage = details.message?.trim();
      setError(
        backendMessage && backendMessage !== defaultMessage
          ? `${defaultMessage} ${backendMessage}`
          : defaultMessage,
      );
      setErrorCorrelationId?.(details.correlationId);
    } finally {
      setLoading(false);
    }
  }, [
    buildRequest,
    closeExportDialog,
    currentPage,
    detailOverflowAction,
    exportFormat,
    exportMode,
    includePartialCover,
    includePartialManifest,
    manifest,
    pageNumberingMode,
    pageRange,
    pageSelectionMode,
    requiresOverflowChoice,
    sectionIds,
    selectedPages,
    setError,
    setErrorCorrelationId,
    setLoading,
  ]);

  return {
    exportOpen,
    openExportDialog,
    closeExportDialog,
    exportDialogRef,
    handleExportDialogCancel,
    exportMode,
    setExportMode,
    exportFormat,
    setExportFormat,
    pageNumberingMode,
    setPageNumberingMode,
    includePartialCover,
    setIncludePartialCover,
    includePartialManifest,
    setIncludePartialManifest,
    detailOverflowAction,
    setDetailOverflowAction,
    requiresOverflowChoice,
    runExport,
  };
}
