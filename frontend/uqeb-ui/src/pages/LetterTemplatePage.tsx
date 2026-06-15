import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { letterTemplatesApi } from '../api/services';
import { getApiErrorMessage } from '../utils/apiHelpers';

export default function LetterTemplatePage() {
  const [content, setContent] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    letterTemplatesApi.getFollowUp()
      .then((r) => setContent(r.data.content))
      .catch(() => setError('تعذر تحميل قالب خطاب التعقيب'))
      .finally(() => setLoading(false));
  }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!content.trim()) {
      setError('محتوى القالب مطلوب');
      return;
    }
    setSaving(true);
    setError('');
    setMessage('');
    try {
      const res = await letterTemplatesApi.updateFollowUp(content);
      setContent(res.data.content);
      setMessage('تم حفظ قالب خطاب التعقيب بنجاح.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <div className="loading">جاري التحميل...</div>;

  return (
    <div dir="rtl">
      <div className="page-header">
        <h2 className="page-title">قالب خطاب التعقيب</h2>
      </div>

      <p className="text-muted" style={{ marginBottom: '1rem' }}>
        المتغيرات المتاحة: {'{IncomingNumber}'} {'{IncomingDate}'} {'{Subject}'} {'{TargetEntity}'} {'{TodayDate}'}
      </p>

      {message && <div className="alert alert-success">{message}</div>}
      {error && <div className="alert alert-error">{error}</div>}

      <div className="card">
        <form onSubmit={submit}>
          <div className="form-group">
            <label>نص القالب</label>
            <textarea
              className="follow-up-letter-body"
              rows={16}
              value={content}
              onChange={(e) => setContent(e.target.value)}
            />
          </div>
          <div className="form-actions">
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'جاري الحفظ...' : 'حفظ القالب'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
