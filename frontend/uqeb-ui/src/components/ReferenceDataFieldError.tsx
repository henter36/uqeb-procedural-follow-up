type FieldErrorProps = {
  message?: string;
};

export function FieldError({ message }: Readonly<FieldErrorProps>) {
  return message ? <div className="field-error">{message}</div> : null;
}
