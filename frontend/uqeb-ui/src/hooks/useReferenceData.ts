import { useContext } from 'react';
import { ReferenceDataContext } from '../context/referenceDataStore';

export function useReferenceData() {
  const ctx = useContext(ReferenceDataContext);
  if (!ctx) throw new Error('useReferenceData must be used within ReferenceDataProvider');
  return ctx;
}
