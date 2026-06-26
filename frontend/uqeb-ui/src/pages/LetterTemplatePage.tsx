import { useCallback, useEffect, useMemo, useState } from 'react';
import { letterTemplatesApi } from '../api/services';
import type { LetterTemplate, LetterTemplateVariable } from '../api/types';
import { getApiErrorMessage } from '../utils/apiHelpers';
import { letterTemplateTypeLabels } from '../utils/followUpPrintLabels';
import { sanitizeFullDocumentHtml } from '../utils/sanitizePrintHtml';
import {
  Alert, LoadingInline, PageHeader, EmptyState, ErrorState,
} from '../components/ui';

type EditorState = {
  name: string;
  description: string;
  content: string;
  isActive: boolean;
  templateType: LetterTemplate['templateType'];
};

const EMPTY_EDITOR: EditorState = {
  name: '',
  description: '',
  content: '',
  isActive: true,
  templateType: 'FollowUp',
};

function snapshotEditor(state: EditorState): string {
  return JSON.stringify(state);
}

export default function LetterTemplatePage() {
  const [templates, setTemplates] = useState<LetterTemplate[]>([]);
  const [variables, setVariables] = useState<LetterTemplateVariable[]>([]);
  const [selectedId, setSelectedId] = useState<number | 'new' | null>(null);
  const [editor, setEditor] = useState<EditorState>(EMPTY_EDITOR);
  const [savedSnapshot, setSavedSnapshot] = useState(snapshotEditor(EMPTY_EDITOR));
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [previewHtml, setPreviewHtml] = useState('');
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [showVariables, setShowVariables] = useState(false);

  const isDirty = snapshotEditor(editor) !== savedSnapshot;

  const loadPreview = useCallback(async () => {
    if (selectedId == null) return;
    setPreviewLoading(true);
    setPreviewError('');
    try {
      const res = await letterTemplatesApi.preview({
        name: editor.name.trim() || 'قالب جديد',
        description: editor.description.trim() || undefined,
        content: editor.content,
        templateType: editor.templateType,
      });
      setPreviewHtml(sanitizeFullDocumentHtml(res.data.html));
    } catch (err: unknown) {
      setPreviewError(getApiErrorMessage(err) || 'تعذر بناء معاينة الخطاب.');
    } finally {
      setPreviewLoading(false);
    }
  }, [editor, selectedId]);

  useEffect(() => {
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      if (!isDirty) return;
      event.preventDefault();
    };
    globalThis.addEventListener('beforeunload', onBeforeUnload);
    return () => globalThis.removeEventListener('beforeunload', onBeforeUnload);
  }, [isDirty]);

  const loadTemplates = useCallback(async () => {
    const [listRes, varsRes] = await Promise.all([
      letterTemplatesApi.list(),
      letterTemplatesApi.getVariables(),
    ]);
    setTemplates(listRes.data);
    setVariables(varsRes.data);
    return listRes.data;
  }, []);

  // Auto-refresh preview after user stops typing
  useEffect(() => {
    if (selectedId == null) return undefined;
    const timer = globalThis.setTimeout(() => {
      loadPreview().catch(() => undefined);
    }, 800);
    return () => globalThis.clearTimeout(timer);
  }, [loadPreview, selectedId]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [listRes, varsRes] = await Promise.all([
          letterTemplatesApi.list(),
          letterTemplatesApi.getVariables(),
        ]);
        if (cancelled) return;
        const items = listRes.data;
        setTemplates(items);
        setVariables(varsRes.data);
        if (items.length > 0) {
          const initial = items.find((t) => t.isDefault) ?? items[0];
          setSelectedId(initial.id);
          const nextEditor: EditorState = {
            name: initial.name,
            description: initial.description ?? '',
            content: initial.content,
            isActive: initial.isActive,
            templateType: initial.templateType,
          };
          setEditor(nextEditor);
          setSavedSnapshot(snapshotEditor(nextEditor));
        }
      } catch {
        if (!cancelled) setError('تعذر تحميل قوالب خطاب التعقيب');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const selectTemplate = (template: LetterTemplate) => {
    if (isDirty && !globalThis.confirm('لديك تغييرات غير محفوظة. هل تريد تجاهلها؟')) {
      return;
    }
    setSelectedId(template.id);
    const nextEditor: EditorState = {
      name: template.name,
      description: template.description ?? '',
      content: template.content,
      isActive: template.isActive,
      templateType: template.templateType,
    };
    setEditor(nextEditor);
    setSavedSnapshot(snapshotEditor(nextEditor));
    setMessage('');
    setError('');
    setPreviewHtml('');
    setPreviewError('');
  };

  const startNewTemplate = () => {
    if (isDirty && !globalThis.confirm('لديك تغييرات غير محفوظة. هل تريد تجاهلها؟')) {
      return;
    }
    setSelectedId('new');
    const nextEditor: EditorState = {
      ...EMPTY_EDITOR,
      content: editor.content || templates.find((t) => t.isDefault)?.content || '',
    };
    setEditor(nextEditor);
    setSavedSnapshot(snapshotEditor(nextEditor));
    setMessage('');
    setError('');
    setPreviewHtml('');
    setPreviewError('');
  };

  const handleSave = async () => {
    if (!editor.name.trim()) {
      setError('اسم القالب مطلوب');
      return;
    }
    if (!editor.content.trim()) {
      setError('محتوى القالب مطلوب');
      return;
    }
    setSaving(true);
    setError('');
    setMessage('');
    try {
      if (selectedId === 'new') {
        const res = await letterTemplatesApi.create({
          name: editor.name.trim(),
          description: editor.description.trim() || undefined,
          content: editor.content,
          isActive: editor.isActive,
          templateType: editor.templateType,
        });
        await loadTemplates();
        setSelectedId(res.data.id);
        const nextEditor: EditorState = {
          name: res.data.name,
          description: res.data.description ?? '',
          content: res.data.content,
          isActive: res.data.isActive,
          templateType: res.data.templateType,
        };
        setEditor(nextEditor);
        setSavedSnapshot(snapshotEditor(nextEditor));
        setMessage('تم إنشاء القالب بنجاح.');
      } else if (typeof selectedId === 'number') {
        const res = await letterTemplatesApi.update(selectedId, {
          name: editor.name.trim(),
          description: editor.description.trim() || undefined,
          content: editor.content,
          isActive: editor.isActive,
          templateType: editor.templateType,
        });
        await loadTemplates();
        const nextEditor: EditorState = {
          name: res.data.name,
          description: res.data.description ?? '',
          content: res.data.content,
          isActive: res.data.isActive,
          templateType: res.data.templateType,
        };
        setEditor(nextEditor);
        setSavedSnapshot(snapshotEditor(nextEditor));
        setMessage('تم حفظ القالب بنجاح.');
      }
    } catch (err: unknown) {
      setError(getApiErrorMessage(err) || 'تعذر إنشاء القالب لأن نوع القالب غير صالح أو البيانات ناقصة.');
    } finally {
      setSaving(false);
    }
  };

  const handleCopy = async () => {
    if (typeof selectedId !== 'number') return;
    setSaving(true);
    setError('');
    try {
      const res = await letterTemplatesApi.copy(selectedId);
      await loadTemplates();
      selectTemplate(res.data);
      setMessage('تم نسخ القالب.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  const handleSetDefault = async () => {
    if (typeof selectedId !== 'number') return;
    setSaving(true);
    setError('');
    try {
      await letterTemplatesApi.setDefault(selectedId);
      await loadTemplates();
      setMessage('تم تعيين القالب كافتراضي.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (typeof selectedId !== 'number') return;
    if (!globalThis.confirm('هل أنت متأكد من حذف هذا القالب؟')) return;
    setSaving(true);
    setError('');
    try {
      await letterTemplatesApi.delete(selectedId);
      const items = await loadTemplates();
      if (items.length > 0) {
        selectTemplate(items.find((t) => t.isDefault) ?? items[0]);
      } else {
        setSelectedId(null);
        setEditor(EMPTY_EDITOR);
        setSavedSnapshot(snapshotEditor(EMPTY_EDITOR));
      }
      setMessage('تم حذف القالب.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  const insertVariable = (name: string) => {
    setEditor((prev) => ({ ...prev, content: `${prev.content}{${name}}` }));
  };

  const selectedTemplate = useMemo(
    () => (typeof selectedId === 'number' ? templates.find((t) => t.id === selectedId) : null),
    [selectedId, templates],
  );

  if (loading) {
    return (
      <div dir="rtl">
        <PageHeader title="قوالب خطاب التعقيب" subtitle="إدارة قوالب خطابات التعقيب والمتغيرات" />
        <LoadingInline label="جاري تحميل القوالب..." />
      </div>
    );
  }

  if (error && templates.length === 0) {
    return (
      <div dir="rtl">
        <PageHeader title="قوالب خطاب التعقيب" subtitle="إدارة قوالب خطابات التعقيب والمتغيرات" />
        <ErrorState title="تعذر التحميل" description={error} />
      </div>
    );
  }

  return (
    <div dir="rtl" className="letter-template-page">
      <PageHeader
        title="قوالب خطاب التعقيب"
        subtitle="إدارة قوالب خطابات التعقيب والمتغيرات"
        actions={(
          <button type="button" className="btn btn-outline" onClick={startNewTemplate}>
            قالب جديد
          </button>
        )}
      />

      {message && <Alert variant="success">{message}</Alert>}
      {error && <Alert variant="error">{error}</Alert>}
      {isDirty && <Alert variant="warning">لديك تغييرات غير محفوظة.</Alert>}

      {/* Main 3-panel layout: قائمة | محرر | معاينة */}
      <div className="letter-template-layout">

        {/* Panel 1: قائمة القوالب */}
        <aside className="letter-template-list">
          <h3>القوالب</h3>
          {templates.length === 0 ? (
            <EmptyState title="لا توجد قوالب" description="أنشئ قالباً جديداً للبدء." />
          ) : (
            <ul className="letter-template-items">
              {templates.map((template) => (
                <li key={template.id}>
                  <button
                    type="button"
                    className={`letter-template-item${selectedId === template.id ? ' active' : ''}`}
                    onClick={() => selectTemplate(template)}
                  >
                    <span className="letter-template-item-name">{template.name}</span>
                    <span className="letter-template-item-meta">
                      {template.isDefault && <span className="badge badge-blue">افتراضي</span>}
                      {!template.isActive && <span className="badge badge-gray">غير نشط</span>}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </aside>

        {/* Panel 2: محرر القالب */}
        <div className="letter-template-editor">
          {selectedId == null ? (
            <EmptyState title="اختر قالباً" description="اختر قالباً من القائمة أو أنشئ قالباً جديداً." />
          ) : (
            <>
              <div className="form-grid">
                <div className="form-group">
                  <label htmlFor="template-name">اسم القالب</label>
                  <input
                    id="template-name"
                    value={editor.name}
                    onChange={(e) => setEditor((prev) => ({ ...prev, name: e.target.value }))}
                  />
                </div>
                <div className="form-group">
                  <label htmlFor="template-type">نوع القالب</label>
                  <select
                    id="template-type"
                    value={editor.templateType}
                    onChange={(e) => setEditor((prev) => ({
                      ...prev,
                      templateType: e.target.value as LetterTemplate['templateType'],
                    }))}
                  >
                    {Object.entries(letterTemplateTypeLabels).map(([value, label]) => (
                      <option key={value} value={value}>{label}</option>
                    ))}
                  </select>
                </div>
                <div className="form-group full-width">
                  <label htmlFor="template-description">الوصف</label>
                  <input
                    id="template-description"
                    value={editor.description}
                    onChange={(e) => setEditor((prev) => ({ ...prev, description: e.target.value }))}
                  />
                </div>
                <div className="form-group">
                  <label htmlFor="template-active">
                    <input
                      id="template-active"
                      type="checkbox"
                      checked={editor.isActive}
                      onChange={(e) => setEditor((prev) => ({ ...prev, isActive: e.target.checked }))}
                    />
                    {' '}نشط
                  </label>
                </div>
                <div className="form-group full-width">
                  <label htmlFor="template-content">نص القالب</label>
                  <textarea
                    id="template-content"
                    className="follow-up-letter-body"
                    rows={16}
                    value={editor.content}
                    onChange={(e) => setEditor((prev) => ({ ...prev, content: e.target.value }))}
                  />
                </div>
              </div>

              {/* المتغيرات — قائمة مضغوطة أسفل المحرر */}
              <details className="letter-variables-details" open={showVariables} onToggle={(e) => setShowVariables((e.target as HTMLDetailsElement).open)}>
                <summary className="letter-variables-summary">
                  المتغيرات المتاحة
                  <span className="text-muted"> — انقر لإدراج في نص القالب</span>
                </summary>
                <ul className="letter-variable-list letter-variable-list-compact">
                  {variables.map((variable) => (
                    <li key={variable.name}>
                      <button type="button" className="letter-variable-btn letter-variable-btn-sm" onClick={() => insertVariable(variable.name)}>
                        <code>{`{${variable.name}}`}</code>
                        <span>{variable.arabicDescription}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              </details>

              <div className="form-actions">
                <button type="button" className="btn btn-primary" disabled={saving || !isDirty} onClick={() => { handleSave().catch(() => undefined); }}>
                  {saving ? 'جاري الحفظ...' : 'حفظ'}
                </button>
                <button type="button" className="btn btn-secondary" disabled={previewLoading} onClick={() => { loadPreview().catch(() => undefined); }}>
                  {previewLoading ? 'جاري التحديث...' : 'تحديث المعاينة'}
                </button>
                {typeof selectedId === 'number' && (
                  <>
                    <button type="button" className="btn btn-secondary" disabled={saving} onClick={() => { handleCopy().catch(() => undefined); }}>
                      نسخ
                    </button>
                    {selectedTemplate && !selectedTemplate.isDefault && (
                      <button type="button" className="btn btn-secondary" disabled={saving} onClick={() => { handleSetDefault().catch(() => undefined); }}>
                        تعيين كافتراضي
                      </button>
                    )}
                    <button type="button" className="btn btn-outline" disabled={saving} onClick={() => { handleDelete().catch(() => undefined); }}>
                      حذف
                    </button>
                  </>
                )}
              </div>
            </>
          )}
        </div>

        {/* Panel 3: معاينة القالب */}
        <aside className="letter-template-preview-panel" aria-label="معاينة القالب">
          <div className="preview-panel-header">
            <h3>معاينة الخطاب</h3>
            {previewLoading && <span className="text-muted">جاري التحديث...</span>}
          </div>
          {previewError && <Alert variant="error">{previewError}</Alert>}
          {previewHtml && !previewLoading ? (
            <iframe
              title="معاينة القالب"
              className="letter-template-preview-frame"
              srcDoc={previewHtml}
              sandbox="allow-same-origin"
            />
          ) : (
            !previewLoading && (
              <EmptyState
                title="لا توجد معاينة"
                description={selectedId != null ? 'ستظهر المعاينة تلقائياً أو اضغط «تحديث المعاينة».' : 'اختر قالباً لعرض المعاينة.'}
              />
            )
          )}
          {previewLoading && (
            <div className="preview-loading-overlay">
              <LoadingInline label="جاري بناء معاينة الخطاب..." />
            </div>
          )}
        </aside>

      </div>
    </div>
  );
}
