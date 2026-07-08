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
import RecurringTemplatesPage from './pages/RecurringTemplatesPage';
import UserPermissionsPage from './pages/UserPermissionsPage';
import DataQualityPage from './pages/DataQualityPage';
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
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="TransactionsView">
                  <TransactionsList />
                </ProtectedRoute>
              )}
            />
            <Route
              path="transactions/import"
              element={(
                <ProtectedRoute requiredRoles={['Admin']} requiredPermission="TransactionsCreate">
                  <TransactionImport />
                </ProtectedRoute>
              )}
            />
            <Route
              path="transactions/new"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="TransactionsCreate">
                  <TransactionForm mode="create" />
                </ProtectedRoute>
              )}
            />
            <Route path="transactions/:id" element={<ProtectedRoute requiredPermission="TransactionDetailsView"><TransactionDetail /></ProtectedRoute>} />
            <Route
              path="transactions/:id/edit"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="TransactionsEdit">
                  <TransactionForm mode="edit" />
                </ProtectedRoute>
              )}
            />
            <Route
              path="reports"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="ReportsView">
                  <Reports />
                </ProtectedRoute>
              )}
            />
            <Route
              path="data-quality"
              element={(
                <ProtectedRoute requiredPermission="DataQualityView">
                  <DataQualityPage />
                </ProtectedRoute>
              )}
            />
            {institutionalReportsEnabled() && (
              <Route
                path="report-builder"
                element={(
                  <ProtectedRoute requiredRoles={['Admin']} requiredPermission="ReportsBuild">
                    <ReportBuilder />
                  </ProtectedRoute>
                )}
              />
            )}
            <Route
              path="letter-template"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor']} requiredPermission="ReportsTemplatesManage">
                  <LetterTemplatePage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/eligible"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="FollowUpPrintCreate">
                  <FollowUpPrintEligiblePage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/jobs"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="FollowUpPrintView">
                  <FollowUpPrintJobsPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/jobs/:id"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="FollowUpPrintView">
                  <FollowUpPrintJobDetailPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/pending"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="FollowUpPrintView">
                  <FollowUpPrintPendingPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="follow-up-print/parts/:jobId/:partNumber/print"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="FollowUpPrintExport">
                  <FollowUpPrintPartPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="users"
              element={(
                <ProtectedRoute requiredRoles={['Admin']} requiredPermission="UsersView">
                  <UsersPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="users/permissions"
              element={(
                <ProtectedRoute requiredRoles={['Admin']} requiredPermission="UserPermissionsManage">
                  <UserPermissionsPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="departments"
              element={(
                <ProtectedRoute requiredRoles={['Admin']} requiredPermission="LookupsView">
                  <DepartmentsPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="external-parties"
              element={(
                <ProtectedRoute requiredRoles={['Admin']} requiredPermission="LookupsView">
                  <ExternalPartiesPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="categories"
              element={(
                <ProtectedRoute requiredRoles={['Admin']} requiredPermission="LookupsView">
                  <CategoriesPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="recurring-transaction-templates"
              element={(
                <ProtectedRoute requiredRoles={['Admin']} requiredPermission="ReportsBuild">
                  <RecurringTemplatesPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="security"
              element={(
                <ProtectedRoute requiredRoles={["Admin"]} requiredPermission="SystemSettingsView">
                  <SecurityPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="department-responses"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry', 'DepartmentUser']} requiredPermission="TransactionResponsesEdit">
                  <DepartmentTransactionsPage />
                </ProtectedRoute>
              )}
            />
            <Route
              path="department-responses/review"
              element={(
                <ProtectedRoute requiredRoles={['Admin', 'Supervisor', 'DataEntry']} requiredPermission="TransactionResponsesEdit">
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
