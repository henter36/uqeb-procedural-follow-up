import type { FollowUpDepartmentOption } from '../../api/types';
import { todayLocalIso } from '../../utils/localDate';

export function getDefaultDepartmentIds(options: FollowUpDepartmentOption[]): number[] {
  return options
    .filter((option) => option.isDefaultSelected)
    .map((option) => option.departmentId);
}

export function createInitialFollowUpForm(departmentIds: number[]) {
  return {
    followUpDate: todayLocalIso(),
    notes: '',
    followUpNumber: '',
    departmentIds,
  };
}
