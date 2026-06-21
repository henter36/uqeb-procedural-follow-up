import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { transactionsApi } from '../api/services';
import type {
  ExcelImportCommitResult,
  ExcelImportPreviewResult,
} from '../api/types';
import DateDisplay from '../components/DateDisplay';

type Step = 'upload' | 'preview' | 'result';

const MAX_EXCEL_IMPORT_BYTES = 5 * 1024 * 1024;

function formatErrors(errors: string[]) {
  return errors.join('؛ ');
}

export default function TransactionImport() {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [step, setStep] = useState<Step>('upload');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<ExcelImportPreviewResult | null>(null);
  const [commitResult, setCommitResult] = useState<ExcelImportCommitResult | null>(null);

  const handleFileChange = (selected: File | null) => {
    setPreview(null);
    setCommitResult(null);
    setStep('upload');
    setError(null);

    if (!selected) {
      setFile(null);
      return;
    }

    if (!selected.name.toLowerCase().endsWith('.xlsx')) {
      setFile(null);
      setError('يُقبل ملفات xlsx فقط');
      if (fileInputRef.current) fileInputRef.current.value = '';
      return;
    }

    if (selected.size > MAX_EXCEL_IMPORT_BYTES) {
      setFile(null);
      setError('حجم الملف يتجاوز الحد المسموح للاستيراد (5 ميجابايت)');
      if (fileInputRef.current) fileInputRef.current.value = '';
      return;
    }

    setFile(selected);
  };

  const handlePreview = async () => {
    if (!file) {
      setError('يجب اختيار ملف Excel');
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const res = await transactionsApi.previewExcelImport(file);
      setPreview(res.data);
      setStep('preview');
    } catch (err) {
      setError(isAxiosError(err) ? (err.response?.data as { message?: string })?.message ?? 'تعذر فحص الملف' : 'تعذر فحص الملف');
    } finally {
      setLoading(false);
    }
  };

  const handleCommit = async () => {
    if (!file || !preview?.validRows) return;

    setLoading(true);
    setError(null);
    try {
      const res = await transactionsApi.commitExcelImport(file);
      setCommitResult(res.data);
      setStep('result');
    } catch (err) {
      setError(isAxiosError(err) ? (err.response?.data as { message?: string })?.message ?? 'تعذر اعتماد الاستيراد' : 'تعذر اعتماد الاستيراد');
    } finally {
      setLoading(false);
    }
  };

  const reset = () => {
    setFile(null);
    setPreview(null);
    setCommitResult(null);
    setStep('upload');
    setError(null);
    if (fileInputRef.current) fileInputRef.current.value = '';
  };

  return (
    <div>
      <div className="page-header">
        <h2 className="page-title">استيراد معاملات من Excel</h2>
        <Link to="/transactions" className="btn btn-secondary">العودة للمعاملات</Link>
      </div>

      <div className="card">
        <div className="form-group">
          <label htmlFor="excel-file">ملف Excel (xlsx)</label>
          <input
            id="excel-file"
            ref={fileInputRef}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            onChange={(e) => handleFileChange(e.target.files?.[0] ?? null)}
          />
        </div>

        {file && (
          <p className="text-muted">الملف المحدد: {file.name}</p>
        )}

        {error && <div className="alert alert-error">{error}</div>}

        {step !== 'result' && (
          <div className="form-actions">
            <button
              type="button"
              className="btn btn-primary"
              onClick={handlePreview}
              disabled={!file || loading}
            >
              {loading && step === 'upload' ? 'جاري الفحص...' : 'فحص الملف'}
            </button>
            {step === 'preview' && preview && preview.validRows > 0 && (
              <button
                type="button"
                className="btn btn-success"
                onClick={handleCommit}
                disabled={loading}
              >
                {loading ? 'جاري الاعتماد...' : 'اعتماد الاستيراد'}
              </button>
            )}
          </div>
        )}
      </div>

      {step === 'preview' && preview && (
        <div className="card">
          <h3>نتيجة المعاينة</h3>
          <div className="import-summary">
            <span>إجمالي الصفوف: <strong>{preview.totalRows}</strong></span>
            <span>صفوف صحيحة: <strong className="text-success">{preview.validRows}</strong></span>
            <span>صفوف مرفوضة: <strong className="text-danger">{preview.invalidRows}</strong></span>
          </div>

          <div className="table-responsive">
            <table className="data-table">
              <thead>
                <tr>
                  <th>رقم الصف</th>
                  <th>رقم الخطاب الوارد</th>
                  <th>تاريخ الخطاب الوارد</th>
                  <th>الموضوع</th>
                  <th>الجهة المحال لها</th>
                  <th>الإجراء المتخذ</th>
                  <th>الحالة</th>
                  <th>الأخطاء</th>
                </tr>
              </thead>
              <tbody>
                {preview.rows.map((row) => (
                  <tr key={row.rowNumber} className={row.isValid ? '' : 'row-invalid'}>
                    <td>{row.rowNumber}</td>
                    <td>{row.data?.incomingNumber ?? '—'}</td>
                    <td>
                      {row.data?.incomingDate
                        ? <DateDisplay date={row.data.incomingDate} />
                        : '—'}
                    </td>
                    <td>{row.data?.subject ?? '—'}</td>
                    <td>{row.data?.assignedDepartmentName ?? '—'}</td>
                    <td>{row.data?.actionTaken ?? '—'}</td>
                    <td>
                      <span className={row.isValid ? 'badge badge-green' : 'badge badge-red'}>
                        {row.isValid ? 'صالح' : 'مرفوض'}
                      </span>
                    </td>
                    <td className="error-cell">{row.errors.length ? formatErrors(row.errors) : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {step === 'result' && commitResult && (
        <div className="card">
          <h3>نتيجة الاستيراد</h3>
          <div className="import-summary">
            <span>معاملات مستوردة: <strong className="text-success">{commitResult.importedCount}</strong></span>
            <span>صفوف مرفوضة: <strong className="text-danger">{commitResult.rejectedCount}</strong></span>
          </div>

          {commitResult.rejectedRows.length > 0 && (
            <>
              <h4>الصفوف المرفوضة</h4>
              <div className="table-responsive">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>رقم الصف</th>
                      <th>الأخطاء</th>
                    </tr>
                  </thead>
                  <tbody>
                    {commitResult.rejectedRows.map((row) => (
                      <tr key={row.rowNumber}>
                        <td>{row.rowNumber}</td>
                        <td className="error-cell">{formatErrors(row.errors)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}

          <div className="form-actions">
            <Link to="/transactions" className="btn btn-primary">العودة للمعاملات</Link>
            <button type="button" className="btn btn-secondary" onClick={reset}>
              استيراد ملف آخر
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
