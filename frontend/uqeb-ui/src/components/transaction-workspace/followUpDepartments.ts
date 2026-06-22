import type { FollowUpDepartmentOption } from '../../api/types';

export function getDefaultDepartmentIds(options: FollowUpDepartmentOption[]): number[] {
  return options
    .filter((option) => option.isDefaultSelected)
    .map((option) => option.departmentId);
}

export function createInitialFollowUpForm(departmentIds: number[]) {
  return {
    followUpDate: new Date().toISOString().split('T')[0],
    notes: '',
    followUpNumber: '',
    departmentIds,
  };
}
