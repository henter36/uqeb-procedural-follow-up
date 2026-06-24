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
import SecurityPage from './pages/SecurityPage';
import TransactionImport from './pages/TransactionImport';
import { institutionalReportsEnabled } from './config/institutionalReportsRuntime';

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route element={(
            <ProtectedRoute>
              <ReferenceDataProvider>
                <Layout />
              </ReferenceDataProvider>
            </ProtectedRoute>
          )}>
            <Route index element={<Dashboard />} />
            <Route path="transactions" element={<TransactionsList />} />
            <Route
              path="transactions/import"
              element={(
                <ProtectedRoute requiredRoles={['Admin']}>
                  <TransactionImport />
                </ProtectedRoute>
              )}
            />
            <Route path="transactions/new" element={<TransactionForm mode="create" />} />
            <Route path="transactions/:id" element={<TransactionDetail />} />
            <Route path="transactions/:id/edit" element={<TransactionForm mode="edit" />} />
            <Route path="reports" element={<Reports />} />
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
            <Route path="letter-template" element={<LetterTemplatePage />} />
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
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
