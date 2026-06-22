export function areSortedIdsEqual(left: number[], right: number[]): boolean {
  if (left.length !== right.length) return false;
  const sortedLeft = [...left].sort((a, b) => a - b);
  const sortedRight = [...right].sort((a, b) => a - b);
  return sortedLeft.every((id, index) => id === sortedRight[index]);
}

export function isShallowRecordDirty<T extends Record<string, unknown>>(
  current: T,
  initial: T,
): boolean {
  return (Object.keys(current) as Array<keyof T>).some((key) => current[key] !== initial[key]);
}
