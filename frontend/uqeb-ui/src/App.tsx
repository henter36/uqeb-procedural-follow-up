import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { ReferenceDataProvider } from './context/ReferenceDataProvider';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import TransactionsList from './pages/TransactionsList';
import TransactionForm from './pages/TransactionForm';
import TransactionDetail from './pages/TransactionDetail';
import Reports from './pages/Reports';
import ReportBuilder from './pages/ReportBuilder';
import { UsersPage, DepartmentsPage, ExternalPartiesPage, CategoriesPage } from './pages/AdminPages';
import LetterTemplatePage from './pages/LetterTemplatePage';
import FollowUpPrintEligiblePage from './pages/FollowUpPrintEligiblePage';
import FollowUpPrintJobsPage from './pages/FollowUpPrintJobsPage';
import FollowUpPrintJobDetailPage from './pages/FollowUpPrintJobDetailPage';
import FollowUpPrintPendingPage from './pages/FollowUpPrintPendingPage';
import FollowUpPrintPartPage from './pages/FollowUpPrintPartPage';
import SecurityPage from './pages/SecurityPage';
import TransactionImport from './pages/TransactionImport';
import DepartmentTransactionsPage from './pages/DepartmentTransactionsPage';
import DepartmentResponseReviewPage from './pages/DepartmentResponseReviewPage';
import { institutionalReportsEnabled } from './config/institutionalReportsRuntime';
import { PendingPrintSummaryProvider } from './context/PendingPrintSummaryContext';

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route element={(
            <ProtectedRoute>
              <ReferenceDataProvider>
                <PendingPrintSummaryProvider>
                  <Layout />
                </PendingPrintSummaryProvider>
              </ReferenceDataProvider>
            </ProtectedRoute>
          )}>
            <Route index element={<Dashboard />} />
            <Route
              path="transactions"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <TransactionsList />
                </ProtectedRoute>
              )}
            />
            <Route
              path="transactions/import"
              element={(
                <ProtectedRoute requiredRoles={['Admin']}>
                  <TransactionImport />
                </ProtectedRoute>
              )}
            />
            <Route
              path="transactions/new"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <TransactionForm mode="create" />
                </ProtectedRoute>
              )}
            />
            <Route path="transactions/:id" element={<TransactionDetail />} />
            <Route
              path="transactions/:id/edit"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <TransactionForm mode="edit" />
                </ProtectedRoute>
              )}
            />
            <Route
              path="reports"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <Reports />
                </ProtectedRoute>
              )}
            />
            {institutionalReportsEnabled() && (
              <Route
                path="report-builder"
                element={(
                  <ProtectedRoute requiredRoles={['Admin']}>
                    <ReportBuilder />
                  </ProtectedRoute>
                )}
              />
            )}
            <Route
              path="letter-template"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor']}>
                  <LetterTemplatePage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/eligible"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <FollowUpPrintEligiblePage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/jobs"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <FollowUpPrintJobsPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/jobs/:id"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <FollowUpPrintJobDetailPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/pending"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <FollowUpPrintPendingPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/parts/:jobId/:partNumber/print"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <FollowUpPrintPartPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="users"
              element={(
                <ProtectedRoute requiredRoles={['Admin']}>
                  <UsersPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="departments"
              element={(
                <ProtectedRoute requiredRoles={['Admin']}>
                  <DepartmentsPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="external-parties"
              element={(
                <ProtectedRoute requiredRoles={['Admin']}>
                  <ExternalPartiesPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="categories"
              element={(
                <ProtectedRoute requiredRoles={['Admin']}>
                  <CategoriesPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="security"
              element={(
                <ProtectedRoute requiredRoles={["Admin"]}>
                  <SecurityPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="department-responses"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry', 'DepartmentUser']}>
                  <DepartmentTransactionsPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="department-responses/review"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']}>
                  <DepartmentResponseReviewPage />
                </ProtectedRoute>
              )}
            />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
