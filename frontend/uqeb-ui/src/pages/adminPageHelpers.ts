export function normalizeName(value: string) {
  return value.trim().replace(/\s+/g, ' ').toLowerCase();
}
