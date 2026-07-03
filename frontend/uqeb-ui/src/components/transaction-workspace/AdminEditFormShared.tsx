type AdminEditReasonFieldProps = Readonly<{
  id: string;
  value: string;
  onChange: (reason: string) => void;
}>;

type AdminEditFormActionsProps = Readonly<{
  saving: boolean;
  dirty: boolean;
  reason: string;
  onCancel: () => void;
}>;

export function AdminEditAuditHint() {
  return (
    <p className="text-muted workspace-form-hint">
      هذا النموذج للتصحيح الإداري فقط. كل تعديل يُسجَّل في سجل التدقيق مع السبب.
    </p>
  );
}

export function AdminEditReasonField({ id, value, onChange }: AdminEditReasonFieldProps) {
  return (
    <div className="form-group full-width">
      <label htmlFor={id}>سبب التعديل *</label>
      <input
        id={id}
        required
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="أدخل سبب التصحيح الإداري..."
      />
    </div>
  );
}

export function AdminEditFormActions({
  saving,
  dirty,
  reason,
  onCancel,
}: AdminEditFormActionsProps) {
  return (
    <div className="form-actions">
      <button type="submit" className="btn btn-primary" disabled={saving || !dirty || !reason.trim()}>
        {saving ? 'جارٍ الحفظ...' : 'حفظ التصحيح'}
      </button>
      <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
    </div>
  );
}
