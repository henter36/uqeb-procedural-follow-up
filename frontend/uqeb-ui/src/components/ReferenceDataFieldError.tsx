export function FieldError({ message }: { message?: string }) {
  return message ? <div className="field-error">{message}</div> : null;
}
