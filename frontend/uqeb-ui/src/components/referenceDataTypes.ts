export type ReferenceListParams = {
  search: string;
  status: string;
  sortBy: string;
  sortDesc: boolean;
  page: number;
  pageSize: number;
};

export const defaultListParams = (): ReferenceListParams => ({
  search: '',
  status: 'all',
  sortBy: 'name',
  sortDesc: false,
  page: 1,
  pageSize: 20,
});
